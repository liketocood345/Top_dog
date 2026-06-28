using System;
using TopDog.Client;
using TopDog.Sim.Realtime;
using UnityEngine;
/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/TACTICAL_VIEW.md §5 zoom/orbit 禁 pan
 * 本文件: TacticalViewportCamera.cs — 战术透视虚拟相机
 * 【机制要点】
 * · 环绕注视点 orbit；滚轮改 ViewDistance（非缩放图标）
 * · UI marker 由世界坐标透视投影到屏幕
 * 【关联】TacticalViewportPresenter · IViewportCameraCommands · TacticalViewportInputOverlay
 * ══
 */



// liketoc0de345
// liketocoode3a5
namespace TopDog.Client.Tactical;

/// <summary>
/// 实时交战透视虚拟相机：绕注视点 orbit；zoom 改变相机与注视点距离。
/// 单位图标为固定像素 UITK，屏幕位置由世界坐标透视投影决定。
/// </summary>
public sealed class TacticalViewportCamera : MonoBehaviour, IViewportCameraCommands
{
    public const float BaseFieldOfViewDeg = BattlefieldSceneProxyService.TacticalEdgeBaseFovDeg;

    private const float ZoomFactor = 1.15f;
    private const float DefaultViewDistanceM = 40_000f;
    private const float MinViewDistanceM = 400f;
    private const float MaxViewDistanceM = 600_000f;
    private const float OrbitStepRad = 0.12f;
    public const float DefaultOrbitPitchRad = Mathf.PI * 0.5f;
    /// <summary>相对默认俯视可 orbit 的半幅（约 ±77°）。</summary>
    private const float OrbitPitchSpanRad = 1.35f;
    private const float DepthEpsilon = 0.01f;

    public float ViewDistance { get; private set; } = DefaultViewDistanceM;
    public float OrbitYawRad { get; private set; }
    public float OrbitPitchRad { get; private set; } = DefaultOrbitPitchRad;
    public float VerticalFovDeg => ClientGameSettings.CombatVerticalFovDeg;

    /// <summary>诊断用：默认距离 / 当前距离（拉近 &gt; 1）。</summary>
    public float ZoomScale => DefaultViewDistanceM / Mathf.Max(ViewDistance, MinViewDistanceM);

    public Func<BattlefieldState?>? ActiveBattlefieldProvider { get; set; }

    public readonly struct ScreenProjection
    {
        public readonly float CenterX;
        public readonly float CenterY;
        public readonly float DirX;
        public readonly float DirY;
        public readonly bool InFront;
        public readonly bool OnScreen;

        public ScreenProjection(
            float centerX,
            float centerY,
            float dirX,
            float dirY,
            bool inFront,
            bool onScreen)
        {
            CenterX = centerX;
            CenterY = centerY;
            DirX = dirX;
            DirY = dirY;
            InFront = inFront;
            OnScreen = onScreen;
        }
    }

    public void WorldOffsetToViewSpace(float dx, float dy, float dz, out float vx, out float vy, out float vz)
    {
        var cosY = Mathf.Cos(OrbitYawRad);
        var sinY = Mathf.Sin(OrbitYawRad);
        var rx = dx * cosY - dz * sinY;
        var rz = dx * sinY + dz * cosY;
        var ry = dy;
        var cosP = Mathf.Cos(OrbitPitchRad);
        // liketocoode34e
        var sinP = Mathf.Sin(OrbitPitchRad);
        vx = rx;
        vy = ry * cosP - rz * sinP;
        vz = ry * sinP + rz * cosP;
    }

    /// <summary>将相对注视点的世界偏移透视投影到视口像素（图标尺寸不变）。</summary>
    public ScreenProjection ProjectWorldOffset(
        float dx,
        float dy,
        float dz,
        float viewportWidth,
        float viewportHeight,
        float edgePad = 0f,
        float markerHalf = 0f)
    {
        WorldOffsetToViewSpace(dx, dy, dz, out var vx, out var vy, out var vz);
        var halfW = viewportWidth * 0.5f;
        var halfH = viewportHeight * 0.5f;
        var maxPxX = halfW - edgePad - markerHalf;
        var maxPxY = halfH - edgePad - markerHalf;
        var depth = vz + ViewDistance;

        if (depth > DepthEpsilon)
        {
            var aspect = viewportWidth / Mathf.Max(viewportHeight, 1f);
            var tanHalf = Mathf.Tan(VerticalFovDeg * Mathf.Deg2Rad * 0.5f);
            var ndcX = vx / (depth * tanHalf * aspect);
            var ndcY = vy / (depth * tanHalf);
            var cx = halfW + ndcX * halfW;
            var cy = halfH - ndcY * halfH;
            var onScreen = Mathf.Abs(ndcX) <= 1f && Mathf.Abs(ndcY) <= 1f;
            if (!onScreen)
            {
                ClampNdcToEdge(ref ndcX, ref ndcY, maxPxX / halfW, maxPxY / halfH);
                cx = halfW + ndcX * maxPxX;
                cy = halfH - ndcY * maxPxY;
            }

            return new ScreenProjection(cx, cy, cx - halfW, cy - halfH, true, onScreen);
        }

        var backX = -vx;
        var backY = -vy;
        float ndcBackX;
        float ndcBackY;
        if (Mathf.Abs(backX) < 1e-5f && Mathf.Abs(backY) < 1e-5f)
        {
            ndcBackX = 0f;
            ndcBackY = -1f;
        }
        else
        {
            var backScale = Mathf.Min(
                1f / Mathf.Max(Mathf.Abs(backX), 1e-5f),
                1f / Mathf.Max(Mathf.Abs(backY), 1e-5f));
            ndcBackX = backX * backScale;
            ndcBackY = backY * backScale;
        }

        var behindCx = halfW + ndcBackX * maxPxX;
        var behindCy = halfH - ndcBackY * maxPxY;
        return new ScreenProjection(
            behindCx,
            behindCy,
            behindCx - halfW,
            behindCy - halfH,
            false,
            false);
    }

    public void ZoomIn()
    {
        ViewDistance /= ZoomFactor;
        if (ViewDistance < MinViewDistanceM)
        {
            ViewDistance = MinViewDistanceM;
        }
    }

    public void ZoomOut()
    {
        ViewDistance *= ZoomFactor;
        if (ViewDistance > MaxViewDistanceM)
        {
            ViewDistance = MaxViewDistanceM;
        }
    }

    public void OrbitLeft() => OrbitYawRad += OrbitStepRad;
    public void OrbitRight() => OrbitYawRad -= OrbitStepRad;
    public void OrbitUp() =>
        OrbitPitchRad = Mathf.Clamp(
            OrbitPitchRad + OrbitStepRad,
            DefaultOrbitPitchRad - OrbitPitchSpanRad,
            DefaultOrbitPitchRad + OrbitPitchSpanRad);

    public void OrbitDown() =>
        OrbitPitchRad = Mathf.Clamp(
            OrbitPitchRad - OrbitStepRad,
            DefaultOrbitPitchRad - OrbitPitchSpanRad,
            DefaultOrbitPitchRad + OrbitPitchSpanRad);
    public void PanLeft() { }
    public void PanRight() { }
    public void PanUp() { }
    // liketoco0de345
    public void PanDown() { }

    public void FrameAll() => ViewDistance = DefaultViewDistanceM;

    public void ResetToTopDown(BattlefieldState? bf)
    {
        OrbitYawRad = 0f;
        OrbitPitchRad = DefaultOrbitPitchRad;
        if (bf == null || bf.units.Count == 0)
        {
            ViewDistance = DefaultViewDistanceM;
            return;
        }

        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minY = float.MaxValue;
        var maxY = float.MinValue;
        foreach (var u in bf.units)
        {
            if (u.IsDestroyed() || BattlefieldSceneProxyService.IsSceneProxy(u))
            {
                continue;
            }

            minX = Mathf.Min(minX, u.x);
            maxX = Mathf.Max(maxX, u.x);
            minY = Mathf.Min(minY, u.y);
            maxY = Mathf.Max(maxY, u.y);
        }

        var span = Mathf.Max(maxX - minX, maxY - minY, 500f);
        var tanHalf = Mathf.Tan(VerticalFovDeg * Mathf.Deg2Rad * 0.5f);
        ViewDistance = Mathf.Clamp(span / (2f * tanHalf) * 1.25f, MinViewDistanceM, MaxViewDistanceM);
    }

    public void ResetView() => ResetToTopDown(ActiveBattlefieldProvider?.Invoke());

    private static void ClampNdcToEdge(ref float ndcX, ref float ndcY, float maxNdcX, float maxNdcY)
    {
        var absX = Mathf.Abs(ndcX);
        var absY = Mathf.Abs(ndcY);
        if (absX < 1e-5f && absY < 1e-5f)
        {
            ndcX = 0f;
            ndcY = maxNdcY;
            return;
        }

        var scale = Mathf.Min(maxNdcX / Mathf.Max(absX, 1e-5f), maxNdcY / Mathf.Max(absY, 1e-5f));
        ndcX *= scale;
        ndcY *= scale;
    }
// liketocoode3a5
}
