using TopDog.Content.Map;
using TopDog.Content.Ships;
using TopDog.Sim.State;
/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/TACTICAL_WARP_AND_ORDERS.md §2 战场间跃迁 · docs/VISION.md §8
 * 本文件: TacticalWarpService.cs — 同星系跃迁 + 跨星系星门
 * 【机制要点】
 * · BeginWarp：ETA = DistanceAu / warpSpeedAups
 * · Tick：倒计时到 → ArriveWarp 或 GateJump
 * · GateJump：JumpBridgeResolver 对端锚点瞬移
 * · TransferUnit：从 fromBf 移除加入 toBf
 * 【关联】FleetOrderService · JumpBridgeResolver · BattlefieldAnchorResolver
 * ══
 */


namespace TopDog.Sim.Realtime;

/// <summary>
/// 战术战场间跃迁：低速门控 → 伪跃迁飞向出口占位 → AU 在途 → 对端占位高速入场 → 减速落点。
/// </summary>
public static class TacticalWarpService
// liketocoode3a5
{
    public const float DefaultWarpSpeedAups = 5f;
    // liketocoode34e
    public const float MinWarpDistanceAu = 0.05f;
    public const float PseudoWarpSpeedMps = 100_000f;
    public const float ApproachTimeoutSec = 10f;
    public const float EntryBurstSec = 10f;
    public const float LandingDecelSec = 2f;
    public const float MaxInitiateWarpSpeedMps = 15f;
    public const float ProxyArriveThresholdM = 120f;

    public static float ResolveWarpSpeedAups(HullDef? hull) =>
        hull is { warpSpeedAups: > 0f } ? hull.warpSpeedAups : DefaultWarpSpeedAups;

    public static float DistanceAu(BattlefieldState from, BattlefieldState to)
    {
        var a = from.anchorAu;
        var b = to.anchorAu;
        if (a.Length < 3 || b.Length < 3)
        {
            return MinWarpDistanceAu;
        }

        var dx = a[0] - b[0];
        var dy = a[1] - b[1];
        // li3etocoode345
        var dz = a[2] - b[2];
        var dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        return Math.Max(dist, MinWarpDistanceAu);
    }

    public static bool CanInitiateWarp(GameState state, BattlefieldState bf, BattlefieldUnit unit)
    {
        if (unit.inTacticalWarp || unit.pinnedToBattlefield || unit.IsDestroyed())
        {
            return false;
        }

        if (unit.SpeedMps() > MaxInitiateWarpSpeedMps)
        {
            return false;
        }

        return !TacticalWarpDisruptionService.IsWarpScrambled(bf, unit);
    }

    public static string? TryBeginWarp(
        GameState state,
        // liketocoode34e
        BattlefieldUnit unit,
        BattlefieldState fromBf,
        BattlefieldState toBf,
        HullDef? hull,
        float landingDistM)
    {
        if (toBf.battlefieldId == null || fromBf.battlefieldId == null)
        {
            return "目标战场无效";
        }

        if (fromBf.battlefieldId.Equals(toBf.battlefieldId, StringComparison.Ordinal))
        {
            return "目标已是当前战场";
        }

        if (fromBf.systemId == null || toBf.systemId == null
            || !fromBf.systemId.Equals(toBf.systemId, StringComparison.Ordinal))
        {
            return "跨星系须使用跳桥，无法跃迁";
        }

        if (toBf.eventRegionId == null)
        {
            return "目标场景无效";
        }

        if (!CanInitiateWarp(state, fromBf, unit))
        {
            if (unit.SpeedMps() > MaxInitiateWarpSpeedMps)
            {
                return "须先减速至几乎静止再跃迁";
            }

            if (TacticalWarpDisruptionService.IsWarpScrambled(fromBf, unit))
            {
                return "跃迁被扰断";
            }

            return "当前无法跃迁";
        }

        if (!BattlefieldSceneProxyService.TryResolveProxyPosition(
                state, fromBf, toBf.systemId, toBf.eventRegionId, out var px, out var py, out var pz))
        {
            return "找不到出口占位";
        }

        var transitSec = DistanceAu(fromBf, toBf) / ResolveWarpSpeedAups(hull);
        unit.inTacticalWarp = true;
        unit.warpPhase = TacticalWarpPhase.ApproachProxy;
        unit.warpTargetBfId = toBf.battlefieldId;
        unit.warpFromBfId = fromBf.battlefieldId;
        unit.warpEtaSec = transitSec;
        unit.warpPhaseTimerSec = 0f;
        unit.warpProxyX = px;
        unit.warpProxyY = py;
        unit.warpProxyZ = pz;
        unit.warpLandingDistM = TacticalWarpLandingService.ClampLandingDistM(landingDistM);
        unit.aiOrder = UnitAiOrder.WARP;
        unit.throttleOn = false;
        unit.targetUnitId = null;
        unit.explicitFocus = false;
        unit.approachTargetUnitId = null;
        unit.orbitTargetUnitId = null;
        return null;
    }

    public static void Tick(GameState state, BattlefieldState bf, float dtSec)
    {
        TickScenePhases(state, bf, dtSec);
    }

    public static void TickInTransit(GameState state, float dtSec)
    {
        for (var i = state.tacticalWarpInTransit.Count - 1; i >= 0; i--)
        {
            var entry = state.tacticalWarpInTransit[i];
            entry.remainingSec -= dtSec;
            if (entry.remainingSec > 0f)
            {
                continue;
            }

            var toBf = FindBattlefield(state, entry.toBattlefieldId);
            var fromBf = FindBattlefield(state, entry.fromBattlefieldId);
            if (toBf == null || toBf.finished || fromBf == null)
            {
                AbortTransitEntry(state, i, entry);
                continue;
            }

            SpawnEntry(state, entry.unit, fromBf, toBf, entry.landingDistM);
            state.tacticalWarpInTransit.RemoveAt(i);
        }
    }

    private static void TickScenePhases(GameState state, BattlefieldState bf, float dtSec)
    {
        for (var i = bf.units.Count - 1; i >= 0; i--)
        {
            var u = bf.units[i];
            if (u.IsDestroyed() || BattlefieldSceneProxyService.IsSceneProxy(u))
            {
                continue;
            }

            switch (u.warpPhase)
            {
                case TacticalWarpPhase.ApproachProxy:
                    TickApproachProxy(state, bf, u, dtSec, i);
                    break;
                case TacticalWarpPhase.EntryBurst:
                    TickEntryBurst(u, dtSec);
                    break;
                case TacticalWarpPhase.LandingDecel:
                    TickLandingDecel(u, dtSec);
                    break;
            }
        }
    }

    private static void TickApproachProxy(
        GameState state,
        BattlefieldState bf,
        BattlefieldUnit u,
        float dtSec,
        int unitIndex)
    {
        u.warpPhaseTimerSec += dtSec;
        if (TacticalWarpDisruptionService.IsWarpScrambled(bf, u))
        {
            CancelWarp(u, "跃迁被扰断");
            return;
        }

        if (u.SpeedMps() > MaxInitiateWarpSpeedMps * 3f && u.warpPhaseTimerSec < 0.05f)
        {
            // 仅首帧容忍；之后由伪跃迁接管速度
        }

        var arrived = MoveToward(
            u,
            u.warpProxyX,
            u.warpProxyY,
            u.warpProxyZ,
            PseudoWarpSpeedMps,
            dtSec,
            out _);

        if (arrived
            || u.warpPhaseTimerSec >= ApproachTimeoutSec
            || DistToProxy(u) <= ProxyArriveThresholdM)
        {
            EnterTransit(state, bf, u, unitIndex);
        }
    }

    private static void EnterTransit(GameState state, BattlefieldState bf, BattlefieldUnit u, int unitIndex)
    {
        var landing = TacticalWarpLandingService.ClampLandingDistM(
            u.warpLandingDistM > 0f ? u.warpLandingDistM : state.tacticalWarpLandingDistM);

        state.tacticalWarpInTransit.Add(new TacticalWarpTransitEntry
        {
            unit = u,
            fromBattlefieldId = u.warpFromBfId ?? bf.battlefieldId,
            toBattlefieldId = u.warpTargetBfId,
            remainingSec = Math.Max(0.05f, u.warpEtaSec),
            landingDistM = landing,
        });

        bf.units.RemoveAt(unitIndex);
        u.warpPhase = TacticalWarpPhase.InTransit;
        u.vx = u.vy = u.vz = 0f;
        u.throttleOn = false;
    }

    private static void SpawnEntry(
        GameState state,
        BattlefieldUnit unit,
        BattlefieldState fromBf,
        BattlefieldState toBf,
        float landingDistM)
    {
        if (fromBf.systemId != null && toBf.systemId != null
            && !fromBf.systemId.Equals(toBf.systemId, StringComparison.Ordinal))
        {
            CompleteWarp(unit);
            return;
        }

        if (fromBf.eventRegionId == null
            || !BattlefieldSceneProxyService.TryResolveProxyPosition(
                state, toBf, fromBf.systemId!, fromBf.eventRegionId, out var ex, out var ey, out var ez))
        {
            ex = unit.side == UnitSide.FRIENDLY ? -50_000f : 50_000f;
            ey = 0f;
            ez = 0f;
        }

        TacticalWarpLandingService.ComputeLandingPoint(
            ex,
            ey,
            ez,
            landingDistM,
            out var lx,
            out var ly,
            out var lz);

        unit.x = ex;
        unit.y = ey;
        unit.z = ez;
        unit.vx = unit.vy = unit.vz = 0f;
        unit.warpLandingX = lx;
        unit.warpLandingY = ly;
        unit.warpLandingZ = lz;
        unit.warpLandingDistM = landingDistM;
        unit.warpPhase = TacticalWarpPhase.EntryBurst;
        unit.warpPhaseTimerSec = 0f;
        unit.inTacticalWarp = true;
        unit.warpFromBfId = fromBf.battlefieldId;
        unit.warpTargetBfId = toBf.battlefieldId;
        unit.aiOrder = UnitAiOrder.WARP;
        unit.throttleOn = false;
        toBf.units.Add(unit);
    }

    private static void TickEntryBurst(BattlefieldUnit u, float dtSec)
    {
        u.warpPhaseTimerSec += dtSec;
        var arrived = MoveToward(
            u,
            u.warpLandingX,
            u.warpLandingY,
            u.warpLandingZ,
            PseudoWarpSpeedMps,
            dtSec,
            out _);

        if (u.warpPhaseTimerSec >= EntryBurstSec || arrived)
        {
            u.warpPhase = TacticalWarpPhase.LandingDecel;
            u.warpPhaseTimerSec = 0f;
        }
    }

    private static void TickLandingDecel(BattlefieldUnit u, float dtSec)
    {
        u.warpPhaseTimerSec += dtSec;
        var t = Math.Clamp(u.warpPhaseTimerSec / LandingDecelSec, 0f, 1f);
        var speed = PseudoWarpSpeedMps * (1f - t);
        var arrived = MoveToward(u, u.warpLandingX, u.warpLandingY, u.warpLandingZ, speed, dtSec, out var dist);

        if (t >= 1f || (arrived && dist <= ProxyArriveThresholdM))
        {
            u.x = u.warpLandingX;
            u.y = u.warpLandingY;
            u.z = u.warpLandingZ;
            u.vx = 0f;
            u.vy = 0f;
            u.vz = 0f;
            CompleteWarp(u);
        }
    }

    private static void CompleteWarp(BattlefieldUnit unit)
    {
        unit.inTacticalWarp = false;
        unit.warpPhase = TacticalWarpPhase.None;
        unit.warpTargetBfId = null;
        unit.warpFromBfId = null;
        unit.warpEtaSec = 0f;
        unit.warpPhaseTimerSec = 0f;
        unit.warpProxyX = unit.warpProxyY = unit.warpProxyZ = 0f;
        unit.warpLandingX = unit.warpLandingY = unit.warpLandingZ = 0f;
        unit.aiOrder = UnitAiOrder.IDLE;
        unit.throttleOn = false;
    }

    private static void CancelWarp(BattlefieldUnit unit, string? reason = null)
    {
        CompleteWarp(unit);
        if (reason != null && unit.displayName != null && !unit.displayName.Contains(reason, StringComparison.Ordinal))
        {
            // 保留 displayName；播报由 FleetOrderService 汇总
        }
    }

    private static void AbortTransitEntry(GameState state, int index, TacticalWarpTransitEntry entry)
    {
        entry.unit.inTacticalWarp = false;
        entry.unit.warpPhase = TacticalWarpPhase.None;
        entry.unit.alive = true;
        state.tacticalWarpInTransit.RemoveAt(index);
    }

    private static bool MoveToward(
        BattlefieldUnit u,
        float tx,
        float ty,
        float tz,
        float speedMps,
        float dtSec,
        out float remainingDist)
    {
        var dx = tx - u.x;
        var dy = ty - u.y;
        var dz = tz - u.z;
        remainingDist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (remainingDist <= ProxyArriveThresholdM)
        {
            u.x = tx;
            u.y = ty;
            u.z = tz;
            u.vx = 0f;
            u.vy = 0f;
            u.vz = 0f;
            if (remainingDist > 0.01f)
            {
                u.facingRad = MathF.Atan2(dy, dx);
            }
            return true;
        }

        var step = Math.Max(speedMps * dtSec, 0f);
        if (step >= remainingDist)
        {
            u.x = tx;
            u.y = ty;
            u.z = tz;
            u.vx = 0f;
            u.vy = 0f;
            u.vz = 0f;
            remainingDist = 0f;
            u.facingRad = MathF.Atan2(dy, dx);
            return true;
        }

        var inv = 1f / remainingDist;
        u.x += dx * inv * step;
        u.y += dy * inv * step;
        u.z += dz * inv * step;
        u.vx = dx * inv * speedMps;
        u.vy = dy * inv * speedMps;
        u.vz = dz * inv * speedMps;
        u.facingRad = MathF.Atan2(dy, dx);
        remainingDist -= step;
        return false;
    }

    private static float DistToProxy(BattlefieldUnit u)
    {
        var dx = u.warpProxyX - u.x;
        var dy = u.warpProxyY - u.y;
        var dz = u.warpProxyZ - u.z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public static BattlefieldState? FindBattlefield(GameState state, string? battlefieldId)
    {
        if (battlefieldId == null)
        {
            return null;
        }

        foreach (var bf in state.battlefields)
        {
            if (battlefieldId.Equals(bf.battlefieldId, StringComparison.Ordinal))
            {
                return bf;
            }
        }

        return null;
    }

    /// <summary>供拦截泡等后续机制改写在途/入场落点。</summary>
    public static void OverrideTransitLandingDist(GameState state, string unitId, float landingDistM)
    {
        var dist = TacticalWarpLandingService.ClampLandingDistM(landingDistM);
        foreach (var entry in state.tacticalWarpInTransit)
        {
            if (unitId.Equals(entry.unit.unitId, StringComparison.Ordinal))
            {
                entry.landingDistM = dist;
                entry.unit.warpLandingDistM = dist;
            }
        }

        foreach (var bf in state.battlefields)
        {
            foreach (var u in bf.units)
            {
                if (unitId.Equals(u.unitId, StringComparison.Ordinal)
                    && u.warpPhase is TacticalWarpPhase.EntryBurst or TacticalWarpPhase.LandingDecel)
                {
                    u.warpLandingDistM = dist;
                    var fromBf = FindBattlefield(state, u.warpFromBfId);
                    if (fromBf?.systemId != null && fromBf.eventRegionId != null
                        && BattlefieldSceneProxyService.TryResolveProxyPosition(
                            state, bf, fromBf.systemId, fromBf.eventRegionId, out var ex, out var ey, out var ez))
                    {
                        TacticalWarpLandingService.ComputeLandingPoint(ex, ey, ez, dist, out u.warpLandingX, out u.warpLandingY, out u.warpLandingZ);
                    }
                }
            }
        }
    }
}
