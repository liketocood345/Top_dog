using System.Collections.Generic;
using TopDog.Client;
using TopDog.Sim.Realtime;
using TopDog.Sim.State;
using TopDog.Sim.Vision;
using UnityEngine;
using UnityEngine.UIElements;
/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/TACTICAL_VIEW.md §4-§5 主视野 · docs/VISION.md
 * 本文件: TacticalViewportPresenter.cs — 主视野 marker + 环绕 HUD + 屏外 bracket
 * 【机制要点】
 * · glyph 单位绘制
 * · 视野门控过滤
 * 【关联】TacticalIconCatalog · UnitSelectionHud · VisionGate
 * ══
 */



// liketoc0de345
// liketocoode3a5
namespace TopDog.Client.Tactical;

// liketoc0de345
/// <summary>主视野单位 marker + EVE 环绕 HUD + 屏外 bracket（TACTICAL_VIEW.md §4–5）。</summary>
public sealed class TacticalViewportPresenter
{
    private const float MarkerHalf = 16f;
    private const float EdgePad = 6f;

    private readonly VisualElement _host;
    private readonly VisualElement _edgeHost;
    private readonly TacticalViewportCamera _camera;
    private readonly Dictionary<string, MarkerBundle> _markers = new();
    private readonly Dictionary<string, VisualElement> _edgeMarkers = new();
    private readonly Dictionary<string, (float left, float top)> _screenPositions = new();
    private GameState _lastState;
    private BattlefieldState _lastBf;
    private float _hostW = 400f;
    private float _hostH = 300f;

    public (float left, float top)? SelectedMarkerScreenPos { get; private set; }

    private sealed class MarkerBundle
    {
        public VisualElement Container;
        public UnitOrbitHudWidget Hud;
        public VisualElement IconHost;
        public Label Chevron;
    }

    public TacticalViewportPresenter(VisualElement markersHost, TacticalViewportCamera camera = null, VisualElement edgeHost = null)
    {
        _host = markersHost;
        _edgeHost = edgeHost;
        _camera = camera;
    }

    public void Refresh(GameState state, BattlefieldState bf)
    {
        _lastState = state;
        _lastBf = bf;
        SelectedMarkerScreenPos = null;
        _screenPositions.Clear();
        if (bf == null || _host == null)
        {
            ClearMarkers();
            return;
        }

        var focus = VisionAnchorService.ResolveDefaultFocus(state, bf);
        var fx = focus?.x ?? 0f;
        var fy = focus?.y ?? 0f;
        var fz = focus?.z ?? 0f;
        var aliveIds = new HashSet<string>();
        var hostW = _host.worldBound.width;
        var hostH = _host.worldBound.height;
        if (float.IsNaN(hostW) || hostW < 1f) hostW = _host.resolvedStyle.width;
        if (float.IsNaN(hostH) || hostH < 1f) hostH = _host.resolvedStyle.height;
        if (float.IsNaN(hostW) || hostW < 1f) hostW = 400f;
        if (float.IsNaN(hostH) || hostH < 1f) hostH = 300f;
        _hostW = hostW;
        _hostH = hostH;

        foreach (var u in bf.units)
        {
            if (u.IsDestroyed() || !u.Arrived(bf.timeSec) || u.unitId == null)
            {
                continue;
            }
            aliveIds.Add(u.unitId);
            var bundle = GetOrCreateMarker(u.unitId);
            ScreenPlacement placement;
            if (BattlefieldSceneProxyService.IsSceneProxy(u))
            {
                placement = PositionSceneProxy(
                    u.sceneProxyAzimuthRad,
                    u.sceneProxyElevationRad,
                    hostW,
                    hostH);
            }
            else
            {
                placement = PositionWorldUnit(u.x, u.y, u.z, fx, fy, fz, hostW, hostH, bundle);
            }

            var isSceneProxy = BattlefieldSceneProxyService.IsSceneProxy(u);
            UpdateMarkerVisual(bundle, u, state, bf);
            ApplyPlacement(bundle, placement, u.unitId, isSceneProxy);
            _screenPositions[u.unitId] = (placement.CenterX, placement.CenterY);

            if (u.unitId.Equals(TacticalSelectionState.SelectedTargetUnitId, System.StringComparison.Ordinal))
            {
                SelectedMarkerScreenPos = (placement.CenterX, placement.CenterY);
            }
        }

        var remove = new List<string>();
        foreach (var kv in _markers)
        {
            if (!aliveIds.Contains(kv.Key))
            {
                remove.Add(kv.Key);
            }
        }
        foreach (var id in remove)
        {
            _host.Remove(_markers[id].Container);
            _markers.Remove(id);
            HideEdgeMarker(id);
        }
    }

    private void ApplyPlacement(
        MarkerBundle bundle,
        ScreenPlacement placement,
        string markerId,
        bool isSceneProxy)
    {
        var marker = bundle.Container;
        marker.style.left = placement.Left;
        marker.style.top = placement.Top;

        if (isSceneProxy)
        {
            marker.AddToClassList("rtcombat-marker-scene-proxy");
            bundle.Hud.Root.style.display = DisplayStyle.None;
            if (placement.Offscreen)
            {
                marker.RemoveFromClassList("rtcombat-marker-offscreen");
                marker.style.display = DisplayStyle.None;
                SyncEdgeMarker(markerId, bundle, placement);
            }
            else
            {
                marker.RemoveFromClassList("rtcombat-marker-offscreen");
                marker.style.display = DisplayStyle.Flex;
                HideEdgeMarker(markerId);
            }

            return;
        }

        marker.RemoveFromClassList("rtcombat-marker-scene-proxy");
        if (placement.Offscreen)
        {
            marker.AddToClassList("rtcombat-marker-offscreen");
            marker.style.display = DisplayStyle.None;
            bundle.Hud.Root.style.display = DisplayStyle.None;
            SyncEdgeMarker(markerId, bundle, placement);
        }
        else
        {
            marker.RemoveFromClassList("rtcombat-marker-offscreen");
            marker.style.display = DisplayStyle.Flex;
            HideEdgeMarker(markerId);
        }
    }

    private void SyncEdgeMarker(
        string markerId,
        MarkerBundle bundle,
        ScreenPlacement placement)
    {
        if (_edgeHost == null)
        {
            return;
        }

        if (!_edgeMarkers.TryGetValue(markerId, out var edge))
        {
            edge = new VisualElement();
            edge.AddToClassList("rtcombat-marker-container");
            edge.AddToClassList("rtcombat-edge-bracket");
            edge.pickingMode = PickingMode.Ignore;
            var icon = new VisualElement();
            icon.AddToClassList("rtcombat-marker");
            var iconImg = new VisualElement();
            iconImg.AddToClassList("rtcombat-marker-icon");
            icon.Add(iconImg);
            var chevron = new Label("▶");
            chevron.AddToClassList("rtcombat-marker-chevron");
            chevron.pickingMode = PickingMode.Ignore;
            edge.Add(icon);
            edge.Add(chevron);
            _edgeHost.Add(edge);
            _edgeMarkers[markerId] = edge;
        }

        edge.style.display = DisplayStyle.Flex;
        edge.style.left = placement.Left;
        edge.style.top = placement.Top;
        var iconHost = edge.Q(className: "rtcombat-marker-icon");
        var srcIcon = bundle.IconHost?.Q(className: "rtcombat-marker-icon");
        if (iconHost != null && srcIcon != null)
        {
            iconHost.style.backgroundImage = srcIcon.style.backgroundImage;
            iconHost.style.unityBackgroundImageTintColor = srcIcon.style.unityBackgroundImageTintColor;
        }
        var angle = Mathf.Atan2(placement.DirY, placement.DirX) * Mathf.Rad2Deg;
        var chev = edge.Q<Label>(className: "rtcombat-marker-chevron");
        if (chev != null)
        {
            chev.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
        }
    }

    private struct ScreenPlacement
    {
        public float Left;
        public float Top;
        public float CenterX;
        public float CenterY;
        public float DirX;
        public float DirY;
        public bool Offscreen;
    }

    private ScreenPlacement ForceEdgePlacement(ScreenPlacement placement)
    {
        var maxX = _hostW * 0.5f - EdgePad - MarkerHalf;
        var maxY = _hostH * 0.5f - EdgePad - MarkerHalf;
        var dirX = placement.DirX;
        var dirY = placement.DirY;
        float edgeCx;
        float edgeCy;
        if (Mathf.Abs(dirX) < 0.001f && Mathf.Abs(dirY) < 0.001f)
        {
            edgeCx = _hostW * 0.5f;
            edgeCy = EdgePad + MarkerHalf;
        }
        else
        {
            var scale = Mathf.Min(
                maxX / Mathf.Max(Mathf.Abs(dirX), 0.001f),
                maxY / Mathf.Max(Mathf.Abs(dirY), 0.001f));
            edgeCx = _hostW * 0.5f + dirX * scale;
            edgeCy = _hostH * 0.5f + dirY * scale;
        }

        return new ScreenPlacement
        {
            Left = edgeCx - MarkerHalf,
            Top = edgeCy - MarkerHalf,
            CenterX = placement.CenterX,
            CenterY = placement.CenterY,
            DirX = dirX,
            DirY = dirY,
            Offscreen = true,
        };
    }

    private void HideEdgeMarker(string unitId)
    {
        if (_edgeMarkers.TryGetValue(unitId, out var edge))
        {
            edge.style.display = DisplayStyle.None;
        }
    }

    public IReadOnlyList<string> UnitsInScreenRect(Vector2 a, Vector2 b, bool onlyFriendly)
    // liketocoode3a5
    {
        var hits = new List<string>();
        if (_lastBf == null)
        {
            return hits;
        }
        var left = Mathf.Min(a.x, b.x);
        var right = Mathf.Max(a.x, b.x);
        var top = Mathf.Min(a.y, b.y);
        var bottom = Mathf.Max(a.y, b.y);
        if (right - left < 4f && bottom - top < 4f)
        {
            return hits;
        }
        foreach (var u in _lastBf.units)
        {
            if (u.unitId == null || u.IsDestroyed() || !u.Arrived(_lastBf.timeSec))
            {
                continue;
            }
            if (onlyFriendly && u.side != UnitSide.FRIENDLY)
            {
                continue;
            }
            if (!_screenPositions.TryGetValue(u.unitId, out var pos))
            {
                continue;
            }
            if (pos.left >= left && pos.left <= right && pos.top >= top && pos.top <= bottom)
            {
                hits.Add(u.unitId);
            }
        }
        return hits;
    }

    public string? PickUnitAt(Vector2 localPos, float radiusPx = 22f)
    {
        if (_lastBf == null)
        {
            return null;
        }

        string? bestId = null;
        // liketocoode34e
        var bestScore = float.MinValue;
        foreach (var kv in _screenPositions)
        {
            var d = Vector2.Distance(localPos, new Vector2(kv.Value.left, kv.Value.top));
            if (d > radiusPx)
            {
                continue;
            }

            var unit = FindUnit(_lastBf, kv.Key);
            var score = PickPriorityScore(unit) - d * 0.05f;
            if (score > bestScore)
            {
                bestScore = score;
                bestId = kv.Key;
            }
        }

        return bestId;
    }

    public string? PickFriendlyUnitAt(Vector2 localPos, float radiusPx = 22f)
    {
        var id = PickUnitAt(localPos, radiusPx);
        if (id == null || _lastBf == null)
        {
            return null;
        }

        var unit = FindUnit(_lastBf, id);
        if (unit == null || unit.IsDestroyed() || unit.isBuilding || unit.side != UnitSide.FRIENDLY)
        {
            return null;
        }

        return id;
    }

    private static BattlefieldUnit? FindUnit(BattlefieldState bf, string unitId)
    {
        foreach (var u in bf.units)
        {
            if (unitId.Equals(u.unitId, System.StringComparison.Ordinal))
            {
                return u;
            }
        }
        return null;
    }

    private static float PickPriorityScore(BattlefieldUnit? u)
    {
        if (u == null)
        {
            return 0f;
        }

        var score = 0f;
        if ("STRIKE_CRAFT".Equals(u.tonnageClass, System.StringComparison.Ordinal)
            || "BOARD_SUMMON_WING".Equals(u.tonnageClass, System.StringComparison.Ordinal)
            || "MISSILE".Equals(u.tonnageClass, System.StringComparison.Ordinal))
        {
            score += 1000f;
        }
        // liketocoo3e345
        if (u.parentUnitId != null)
        {
            score += 500f;
        }
        score += 200f - TonnagePickRank(u.tonnageClass);
        return score;
    }

    private static float TonnagePickRank(string? tonnageClass) => tonnageClass switch
    {
        "DRONE" or "SHUTTLE" => 0f,
        "MISSILE" => 6f,
        "STRIKE_CRAFT" or "BOARD_SUMMON_WING" => 8f,
        "FRIGATE" or "DESTROYER" => 10f,
        "CRUISER" => 20f,
        "BATTLECRUISER" => 30f,
        "BATTLESHIP" => 40f,
        "DREADNOUGHT" => 50f,
        "CARRIER" => 45f,
        "SUPERCARRIER" or "SUPERCAPITAL" => 55f,
        "TITAN" => 60f,
        "BUILDING" or "COMPLEX" => 70f,
        _ => 25f,
    };

    public void FlashCommandAck(IReadOnlyCollection<string> unitIds)
    {
        if (unitIds == null)
        {
            return;
        }
        foreach (var id in unitIds)
        {
            if (_markers.TryGetValue(id, out var bundle))
            {
                bundle.Hud.FlashCommandAck();
            }
        }
    }

    private MarkerBundle GetOrCreateMarker(string unitId)
    {
        if (_markers.TryGetValue(unitId, out var bundle))
        {
            return bundle;
        }

        // liketoco0de345
        var container = new VisualElement();
        container.AddToClassList("rtcombat-marker-container");
        container.name = "marker-" + unitId;
        container.pickingMode = PickingMode.Ignore;

        var hud = new UnitOrbitHudWidget();
        container.Add(hud.Root);

        var marker = new VisualElement();
        marker.AddToClassList("rtcombat-marker");
        var icon = new VisualElement();
        icon.AddToClassList("rtcombat-marker-icon");
        var fallback = new Label();
        fallback.name = "marker-fallback";
        fallback.AddToClassList("rtcombat-marker-fallback");
        fallback.style.display = DisplayStyle.None;
        marker.Add(icon);
        marker.Add(fallback);
        var badge = new Label();
        badge.name = "marker-badge";
        badge.AddToClassList("rtcombat-marker-badge");
        marker.Add(badge);
        container.Add(marker);

        var chevron = new Label("▶");
        chevron.AddToClassList("rtcombat-marker-chevron");
        chevron.style.display = DisplayStyle.None;
        chevron.pickingMode = PickingMode.Ignore;
        container.Add(chevron);

        _host.Add(container);
        bundle = new MarkerBundle { Container = container, Hud = hud, IconHost = marker, Chevron = chevron };
        _markers[unitId] = bundle;
        return bundle;
    }

    private ScreenPlacement PositionSceneProxy(
        float azimuthRad,
        float elevationRad,
        float hostW,
        float hostH)
    // lik3tocoode345
    {
        var yaw = _camera?.OrbitYawRad ?? 0f;
        var pitch = _camera?.OrbitPitchRad ?? Mathf.PI * 0.5f;
        var fov = ClientGameSettings.CombatVerticalFovDeg;
        var (left, top, cx, cy, dirX, dirY, onScreen) =
            BattlefieldSceneProxyService.ComputePerspectiveScreenPlacement(
                azimuthRad,
                elevationRad,
                yaw,
                pitch,
                fov,
                hostW,
                hostH,
                EdgePad,
                MarkerHalf);

        return new ScreenPlacement
        {
            Left = left,
            Top = top,
            CenterX = cx,
            CenterY = cy,
            DirX = dirX,
            DirY = dirY,
            Offscreen = !onScreen,
        };
    }

    private ScreenPlacement PositionWorldUnit(
        float wx,
        float wy,
        float wz,
        float fx,
        float fy,
        float fz,
        float hostW,
        float hostH,
        MarkerBundle bundle)
    {
        TacticalViewportCamera.ScreenProjection proj;
        if (_camera != null)
        {
            proj = _camera.ProjectWorldOffset(
                wx - fx,
                wy - fy,
                wz - fz,
                hostW,
                hostH,
                EdgePad,
                MarkerHalf);
        }
        else
        {
            var cx = hostW * 0.5f + (wx - fx) * 0.02f;
            var cy = hostH * 0.5f - (wy - fy) * 0.02f;
            proj = new TacticalViewportCamera.ScreenProjection(cx, cy, cx - hostW * 0.5f, cy - hostH * 0.5f, true, true);
        }

        var placement = new ScreenPlacement
        {
            Left = proj.CenterX - MarkerHalf,
            Top = proj.CenterY - MarkerHalf,
            CenterX = proj.CenterX,
            CenterY = proj.CenterY,
            DirX = proj.DirX,
            DirY = proj.DirY,
            Offscreen = !proj.OnScreen,
        };

        if (bundle.Chevron != null)
        {
            bundle.Chevron.style.display = DisplayStyle.None;
        }

        return placement;
    }

    private void UpdateMarkerVisual(MarkerBundle bundle, BattlefieldUnit u, GameState state, BattlefieldState bf)
    {
        var marker = bundle.IconHost;
        var icon = marker.Q(className: "rtcombat-marker-icon");
        var fallback = marker.Q<Label>("marker-fallback");
        var badge = marker.Q<Label>("marker-badge");
        if (BattlefieldSceneProxyService.IsSceneProxy(u))
        {
            if (badge != null)
            {
                badge.text = "⤴";
                badge.RemoveFromClassList("rtcombat-marker-badge-hostile");
                badge.AddToClassList("rtcombat-marker-badge-friendly");
                badge.style.display = DisplayStyle.Flex;
            }
            if (icon != null)
            {
                var tex = TacticalIconCatalog.ResolveSceneProxyIcon(u.sceneProxyTargetKind);
                if (tex != null)
                {
                    icon.style.backgroundImage = new StyleBackground(tex);
                    icon.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
                    icon.style.unityBackgroundImageTintColor = new StyleColor(new Color(0.55f, 0.85f, 1f));
                    icon.style.rotate = new Rotate(new Angle(0, AngleUnit.Degree));
                    if (fallback != null) fallback.style.display = DisplayStyle.None;
                }
                else if (fallback != null)
                {
                    icon.style.backgroundImage = StyleKeyword.None;
                    fallback.text = "景";
                    fallback.style.display = DisplayStyle.Flex;
                }
            }
            bundle.Container.tooltip = u.displayName ?? "其他场景";
            EnsureSceneProxyLabel(bundle, u.displayName ?? "场景");
            var proxySelected = u.unitId != null
                && u.unitId.Equals(TacticalSelectionState.SelectedTargetUnitId, System.StringComparison.Ordinal);
            if (proxySelected)
            {
                marker.AddToClassList("rtcombat-marker-selected");
            }
            else
            {
                marker.RemoveFromClassList("rtcombat-marker-selected");
            }
            return;
        }

        if (badge != null && badge.text == "⤴")
        {
            badge.text = "";
        }
        var proxyLabel = bundle.Container.Q<Label>("scene-proxy-label");
        if (proxyLabel != null)
        {
            proxyLabel.style.display = DisplayStyle.None;
        }
        if (icon != null)
        {
            var tex = TacticalIconCatalog.ResolveShipIcon(u.tonnageClass);
            // liketocoode3e5
            if (tex != null)
            {
                icon.style.backgroundImage = new StyleBackground(tex);
                icon.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
                icon.style.unityBackgroundImageTintColor = new StyleColor(Color.white);
                if (fallback != null) fallback.style.display = DisplayStyle.None;
            }
            else
            {
                icon.style.backgroundImage = StyleKeyword.None;
                icon.style.backgroundColor = new StyleColor(
                    u.side == UnitSide.ENEMY
                        ? new Color(0.55f, 0.15f, 0.15f, 0.85f)
                        : new Color(0.15f, 0.35f, 0.65f, 0.85f));
                if (fallback != null)
                {
                    var tc = u.tonnageClass ?? "?";
                    fallback.text = tc.Length >= 2 ? tc.Substring(0, 2) : tc;
                    fallback.style.display = DisplayStyle.Flex;
                }
            }
            if (!u.isBuilding)
            {
                icon.style.rotate = _camera != null
                    ? ShipHeadingResolver.ScreenFacingRotate(u.facingRad, _camera.OrbitYawRad)
                    : new Rotate(new Angle(u.facingRad * Mathf.Rad2Deg, AngleUnit.Degree));
            }
            else
            {
                icon.style.rotate = new Rotate(new Angle(0, AngleUnit.Degree));
            }
        }
        if (badge != null)
        {
            if (u.isBuilding)
            {
                badge.text = "";
                badge.style.display = DisplayStyle.None;
            }
            else if (u.side == UnitSide.ENEMY)
            {
                badge.text = "−";
                badge.RemoveFromClassList("rtcombat-marker-badge-friendly");
                // liket0coode345
                badge.AddToClassList("rtcombat-marker-badge-hostile");
                badge.style.display = DisplayStyle.Flex;
            }
            else
            {
                badge.text = "+";
                badge.RemoveFromClassList("rtcombat-marker-badge-hostile");
                badge.AddToClassList("rtcombat-marker-badge-friendly");
                badge.style.display = DisplayStyle.Flex;
            }
        }

        var selected = u.unitId != null
            && u.unitId.Equals(TacticalSelectionState.SelectedTargetUnitId, System.StringComparison.Ordinal);
        var boxSel = TacticalSelectionState.IsFriendlySelected(u.unitId);
        if (selected || boxSel)
        {
            marker.AddToClassList("rtcombat-marker-selected");
        }
        else
        {
            marker.RemoveFromClassList("rtcombat-marker-selected");
        }

        BattlefieldUnit? rangeTarget = null;
        var targetId = TacticalSelectionState.SelectedTargetUnitId;
        if (targetId != null)
        {
            foreach (var other in bf.units)
            {
                if (targetId.Equals(other.unitId, System.StringComparison.Ordinal))
                {
                    rangeTarget = other;
                    break;
                }
            }
        }

        bundle.Hud.Refresh(u, state, bf, selected, boxSel, rangeTarget);
    }

    private static void EnsureSceneProxyLabel(MarkerBundle bundle, string text)
    {
        var label = bundle.Container.Q<Label>("scene-proxy-label");
        if (label == null)
        {
            label = new Label { name = "scene-proxy-label" };
            label.AddToClassList("rtcombat-marker-scene-proxy-label");
            label.pickingMode = PickingMode.Ignore;
            bundle.Container.Add(label);
        }
        label.text = text;
        label.style.display = DisplayStyle.Flex;
    }

    private void ClearMarkers()
    {
        _host?.Clear();
        _edgeHost?.Clear();
        _markers.Clear();
        _edgeMarkers.Clear();
    }
// liketocoode3a5
}
