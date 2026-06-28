#!/usr/bin/env python3
"""Import Second Galaxy cubemap faces into TopDog combat backgrounds.

⚠️ 不要触动 — 实时交战宇宙背景（纯视觉层，不参与游戏逻辑/模拟）。
除非用户明确要求修改本背景功能，否则不要改动本脚本及 CombatBackground* / CombatSpaceBackground* 链路。
"""

from __future__ import annotations

import json
import math
import shutil
from datetime import datetime, timezone
from pathlib import Path

from PIL import Image

SG_ROOT = next(
    p for p in Path(r"e:/sg/decrypted").iterdir() if p.is_dir() and (p / "manifest.json").exists()
)
DEST = Path(r"e:/game_dev/top_dog_unity/TopDog.Unity/Assets/Art/CombatBackgrounds")

# SG export order: +X, +Y, +Z, -X, -Y, -Z → Unity CubemapFace PositiveX..NegativeZ
# Runtime remaps (CombatBackgroundCatalog): U/O/R/S swap +Y/-Y sources; SpaceBoxPRO swap +X/-X.
FACE_SUFFIXES = ("+X", "+Y", "+Z", "-X", "-Y", "-Z")
EQUIRECT_WIDTH = 2048
EQUIRECT_HEIGHT = 1024

MAIN_SETS: dict[str, list[str]] = {
    "U_Skybox_01": [f"U_Skybox_01{s}.png" for s in FACE_SUFFIXES],
    "O_Skybox_01": [f"O_Skybox_01{s}.png" for s in FACE_SUFFIXES],
    "R_Skybox_01": [f"R_Skybox_01{s}.png" for s in FACE_SUFFIXES],
    "S_Skybox_01": [f"S_Skybox_01{s}.png" for s in FACE_SUFFIXES],
    "N_Skybox_Arothe01": [f"SpaceBoxPRO_Arothe01{s}.png" for s in FACE_SUFFIXES],
}

RESERVE_SETS: dict[str, list[str]] = {
    "Wormhole_Perel": [f"SpaceBoxPRO_Perel{s}.png" for s in FACE_SUFFIXES],
    "ProjectXSkyBox": [
        "ProjectXSkyBox_Right.png",
        "ProjectXSkyBox_UP.png",
        "ProjectXSkyBox_Front.png",
        "ProjectXSkyBox_Left.png",
        "ProjectXSkyBox_Down.png",
        "ProjectXSkyBox_Back.png",
    ],
    "Nebula_NuminousGlow": [
        "Nebula_NuminousGlow_2_Left+X.png",
        "Nebula_NuminousGlow_4_Up+Y.png",
        "Nebula_NuminousGlow_0_Front+Z.png",
        "Nebula_NuminousGlow_3_Right-X.png",
        "Nebula_NuminousGlow_5_Down-Y.png",
        "Nebula_NuminousGlow_1_Back-Z.png",
    ],
}


def find_source(name: str) -> Path | None:
    direct = SG_ROOT / name
    if direct.is_file():
        return direct
    alt = SG_ROOT / name.replace("/", "__")
    if alt.is_file():
        return alt
    return None


def _face_uv(x: float, y: float, z: float) -> tuple[int, float, float]:
    """Map world direction to SG face index (+X,+Y,+Z,-X,-Y,-Z) and UV."""
    ax, ay, az = abs(x), abs(y), abs(z)
    if ax >= ay and ax >= az:
        if x >= 0:
            u = (-z / x + 1.0) * 0.5
            v = (-y / x + 1.0) * 0.5
            return 0, u, v
        u = (z / x + 1.0) * 0.5
        v = (-y / x + 1.0) * 0.5
        return 3, u, v
    if ay >= ax and ay >= az:
        if y >= 0:
            u = (x / y + 1.0) * 0.5
            v = (z / y + 1.0) * 0.5
            return 1, u, v
        u = (x / y + 1.0) * 0.5
        v = (-z / y + 1.0) * 0.5
        return 4, u, v
    if z >= 0:
        u = (x / z + 1.0) * 0.5
        v = (-y / z + 1.0) * 0.5
        return 2, u, v
    u = (-x / z + 1.0) * 0.5
    v = (-y / z + 1.0) * 0.5
    return 5, u, v


def _sample_face(face: Image.Image, u: float, v: float) -> tuple[int, int, int, int]:
    w, h = face.size
    px = max(0, min(w - 1, int(u * w)))
    py = max(0, min(h - 1, int((1.0 - v) * h)))
    return face.getpixel((px, py))


def build_equirectangular(face_paths: list[Path], out_path: Path) -> None:
    """Convert SG 6-face cubemap to seamless equirect (matches Unity Skybox sampling)."""
    faces = [Image.open(p).convert("RGBA") for p in face_paths]
    out = Image.new("RGBA", (EQUIRECT_WIDTH, EQUIRECT_HEIGHT))
    pixels = out.load()
    for y in range(EQUIRECT_HEIGHT):
        v = (y + 0.5) / EQUIRECT_HEIGHT
        phi = math.pi * (0.5 - v)
        cos_p = math.cos(phi)
        sin_p = math.sin(phi)
        for x in range(EQUIRECT_WIDTH):
            u = (x + 0.5) / EQUIRECT_WIDTH
            theta = u * 2.0 * math.pi - math.pi
            dx = cos_p * math.sin(theta)
            dy = sin_p
            dz = cos_p * math.cos(theta)
            face_idx, fu, fv = _face_uv(dx, dy, dz)
            pixels[x, y] = _sample_face(faces[face_idx], fu, fv)
    out.save(out_path, optimize=True)
    for im in faces:
        im.close()


def copy_set(set_id: str, file_names: list[str], dest_root: Path) -> dict:
    out_dir = dest_root / set_id
    if out_dir.exists():
        shutil.rmtree(out_dir)
    out_dir.mkdir(parents=True)

    copied: list[str] = []
    face_paths: list[Path] = []
    for name in file_names:
        src = find_source(name)
        if src is None:
            raise FileNotFoundError(f"missing SG texture: {name} (set {set_id})")
        dst = out_dir / src.name
        shutil.copy2(src, dst)
        copied.append(dst.name)
        face_paths.append(dst)

    equirect = out_dir / "equirect.png"
    build_equirectangular(face_paths, equirect)
    return {"id": set_id, "faces": copied, "equirect": equirect.name}


def main() -> int:
    main_dir = DEST / "Main"
    reserve_dir = DEST / "Reserve"
    if main_dir.exists():
        shutil.rmtree(main_dir)
    if reserve_dir.exists():
        shutil.rmtree(reserve_dir)
    main_dir.mkdir(parents=True)
    reserve_dir.mkdir(parents=True)

    manifest = {
        "generated_at": datetime.now(timezone.utc).astimezone().isoformat(),
        "source": str(SG_ROOT),
        "layout": "cubemap_6face_equirect",
        "note": "SG uses Unity Cubemap Skybox (6 faces). TopDog UI uses equirect derived from same faces.",
        "main_sets": [copy_set(k, v, main_dir) for k, v in MAIN_SETS.items()],
        "reserve_sets": [copy_set(k, v, reserve_dir) for k, v in RESERVE_SETS.items()],
    }
    (DEST / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    print(json.dumps({"main": len(manifest["main_sets"]), "reserve": len(manifest["reserve_sets"])}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
