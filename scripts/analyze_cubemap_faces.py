#!/usr/bin/env python3
"""Score cubemap face assignments by edge continuity.

⚠️ 不要触动 — 实时交战宇宙背景（纯视觉层，不参与游戏逻辑/模拟）。
除非用户明确要求修改本背景功能，否则不要改动本脚本及 CombatBackground* / CombatSpaceBackground* 链路。
"""

from __future__ import annotations

import itertools
import math
from pathlib import Path

from PIL import Image

ROOT = Path(r"e:/game_dev/top_dog_unity/TopDog.Unity/Assets/Art/CombatBackgrounds/Main")
SUFFIXES = ("+X", "+Y", "+Z", "-X", "-Y", "-Z")

UNITY_NEIGHBORS = {
    0: {"left": (2, True), "right": (5, True), "top": (1, False), "bottom": (4, False)},
    1: {"left": (3, False), "right": (0, False), "top": (5, True), "bottom": (2, True)},
    2: {"left": (0, False), "right": (3, False), "top": (1, False), "bottom": (4, False)},
    3: {"left": (5, False), "right": (2, False), "top": (1, True), "bottom": (4, True)},
    4: {"left": (3, True), "right": (0, True), "top": (2, True), "bottom": (5, True)},
    5: {"left": (0, True), "right": (3, True), "top": (1, True), "bottom": (4, True)},
}


def load_faces(set_dir: Path) -> dict[str, Image.Image]:
    faces: dict[str, Image.Image] = {}
    for suf in SUFFIXES:
        matches = [p for p in set_dir.glob("*.png") if p.name.endswith(suf + ".png") and p.name != "equirect.png"]
        if not matches:
            raise FileNotFoundError(f"missing {suf} in {set_dir}")
        faces[suf] = Image.open(matches[0]).convert("RGB")
    return faces


def edge_strip(img: Image.Image, side: str, width: int = 4) -> list[tuple[int, int, int]]:
    w, h = img.size
    out: list[tuple[int, int, int]] = []
    if side == "top":
        y_range = range(min(width, h))
        for x in range(0, w, max(1, w // 64)):
            acc = [0, 0, 0]
            for y in y_range:
                p = img.getpixel((x, y))
                for i in range(3):
                    acc[i] += p[i]
            n = len(y_range)
            out.append((acc[0] // n, acc[1] // n, acc[2] // n))
    elif side == "bottom":
        y_range = range(max(0, h - width), h)
        for x in range(0, w, max(1, w // 64)):
            acc = [0, 0, 0]
            for y in y_range:
                p = img.getpixel((x, y))
                for i in range(3):
                    acc[i] += p[i]
            n = len(y_range)
            out.append((acc[0] // n, acc[1] // n, acc[2] // n))
    elif side == "left":
        x_range = range(min(width, w))
        for y in range(0, h, max(1, h // 64)):
            acc = [0, 0, 0]
            for x in x_range:
                p = img.getpixel((x, y))
                for i in range(3):
                    acc[i] += p[i]
            n = len(x_range)
            out.append((acc[0] // n, acc[1] // n, acc[2] // n))
    else:
        x_range = range(max(0, w - width), w)
        for y in range(0, h, max(1, h // 64)):
            acc = [0, 0, 0]
            for x in x_range:
                p = img.getpixel((x, y))
                for i in range(3):
                    acc[i] += p[i]
            n = len(x_range)
            out.append((acc[0] // n, acc[1] // n, acc[2] // n))
    return out


def strip_rms(a: list[tuple[int, int, int]], b: list[tuple[int, int, int]]) -> float:
    n = min(len(a), len(b))
    if n == 0:
        return 1e9
    s = 0.0
    for i in range(n):
        for c in range(3):
            d = a[i][c] - b[i][c]
            s += d * d
    return math.sqrt(s / (n * 3))


def score_assignment(images: list[Image.Image]) -> float:
    strips = {(fi, side): edge_strip(images[fi], side) for fi in range(6) for side in ("top", "bottom", "left", "right")}
    total = 0.0
    for fi, sides in UNITY_NEIGHBORS.items():
        for side, (nj, rev) in sides.items():
            a = strips[(fi, side)]
            opp = {"left": "right", "right": "left", "top": "bottom", "bottom": "top"}[side]
            b = strips[(nj, opp)]
            if rev:
                b = list(reversed(b))
            total += strip_rms(a, b)
    return total


def variants(ordered: list[Image.Image]) -> dict[str, list[Image.Image]]:
    out: dict[str, list[Image.Image]] = {"identity": ordered}
    swap = ordered.copy()
    swap[1], swap[4] = swap[4], swap[1]
    out["swap_y"] = swap
    flip = ordered.copy()
    flip[1] = flip[1].transpose(Image.FLIP_TOP_BOTTOM)
    flip[4] = flip[4].transpose(Image.FLIP_TOP_BOTTOM)
    out["flip_y"] = flip
    sf = swap.copy()
    sf[1] = sf[1].transpose(Image.FLIP_TOP_BOTTOM)
    sf[4] = sf[4].transpose(Image.FLIP_TOP_BOTTOM)
    out["swap_flip_y"] = sf
    return out


def analyze_standard(set_id: str) -> None:
    ordered = [load_faces(ROOT / set_id)[s] for s in SUFFIXES]
    print(f"\n=== {set_id}")
    for name, imgs in variants(ordered).items():
        print(f"  {name:12} score={score_assignment(imgs):.1f}")


def analyze_n(set_id: str) -> None:
    ordered = [load_faces(ROOT / set_id)[s] for s in SUFFIXES]
    print(f"\n=== {set_id} full perm search")
    for name, imgs in variants(ordered).items():
        print(f"  {name:12} score={score_assignment(imgs):.1f}")
    best_score = 1e18
    best_perm = tuple(range(6))
    for perm in itertools.permutations(range(6)):
        sc = score_assignment([ordered[i] for i in perm])
        if sc < best_score:
            best_score = sc
            best_perm = perm
    labels = list(SUFFIXES)
    mapped = [labels[best_perm.index(i)] for i in range(6)]
    print(f"  best_perm score={best_score:.1f} map={mapped}")


def main() -> int:
    for sid in ["U_Skybox_01", "O_Skybox_01", "R_Skybox_01", "S_Skybox_01"]:
        analyze_standard(sid)
    analyze_n("N_Skybox_Arothe01")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
