using TopDog.Content.Ships;
using TopDog.Sim.State;
using TopDog.Sim.Traits;

/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/TACTICAL_WARP_AND_ORDERS.md §1.3 AI/指令运动 · §2 战场间跃迁 · §3 舰队指令表
 * 本文件: FleetOrderService.cs — 实时战术舰队指令（含接近/远离）
 * 【机制要点】
 * · OrderApproach/OrderAway：每 1s 对准 + 满引擎；可选 commandMaintainDistM；不设距不限距 STOP
 * · OrderOrbit：OrbitEntryResolver 几何切入点 + 圆轨道
 * · OrderEnterBuilding：跨星系跳桥无延迟到对端
 * · 集体跃迁 OrderWarp：同星系 AU 伪跃迁；跨星系仅跳桥（OrderEnterBuilding）
 * 【关联】TacticalWarpService · BattlefieldSystem · ShipMotionIntegrator · FleetCommandBar
 * ══
 */

/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/TACTICAL_WARP_AND_ORDERS.md §1.3 AI/指令运动 · §2 战场间跃迁 · §3 舰队指令表
 * 本文件: FleetOrderService.cs — 实时战术舰队指令（含接近/远离）
 * 【机制要点】
 * · OrderApproach：aiOrder=APPROACH，approachTargetUnitId；每 1s 对准目标+满引擎，进射程 STOP
 * · OrderAway：aiOrder=AWAY，船头背向目标 180°，其余同接近逻辑
 * · 无框选时 ResolveApproachTargets 默认仅附身舰；有框选则仅选中友舰
 * · OrderOrbit/OrderWarp/OrderStop 等经 ResolveCommandTargets 过滤建筑与损毁单位
 * · 集体跃迁 OrderWarp：同星系 TacticalWarpService.BeginWarp，跨星系 GateJump
 * 【关联】TacticalWarpService · BattlefieldSystem · ShipMotionIntegrator · FleetCommandBar
 * ══
 */

namespace TopDog.Sim.Realtime;

// liketoc0de345

public static class FleetOrderService
// liketocoode3a5
{
    public static IReadOnlyList<string> LastAcknowledgedUnitIds { get; private set; } = Array.Empty<string>();

    // liketoc0de345

    // liketocoode34e
    private static void SetAck(IEnumerable<BattlefieldUnit> units)
    {
        LastAcknowledgedUnitIds = units
            .Select(u => u.unitId)
            .Where(id => id != null)
            .Cast<string>()
            .ToList();
    }

    private static string FormatOrderAck(int count, string verb) =>
        count > 0 ? $"已下令 {count} 艘{verb}" : $"0 艘执行{verb}";

// liketocoo3e345

    // li3etocoode345

    public static string ToggleAutoFire(GameState state)
    {
        state.autoFireEnabled = !state.autoFireEnabled;
        return state.autoFireEnabled ? "已开启自开火" : "已禁止自开火";
    }

    public static bool TryResolveWarpTargetScene(
        BattlefieldState bf,
        string? selectedUnitId,
        out string systemId,
        out string eventRegionId)
    {
        systemId = "";
        eventRegionId = "";
        var u = FindUnit(bf, selectedUnitId);
        if (!BattlefieldSceneProxyService.TryGetTargetScene(u, out systemId, out eventRegionId))
        {
            return false;
        }

        if (bf.systemId == null || !bf.systemId.Equals(systemId, StringComparison.Ordinal))
        {
            systemId = "";
            eventRegionId = "";
            return false;
        }

        return true;
    }

    public static string? ResolveWarpTargetBattlefieldId(
        GameState state,
        BattlefieldState bf,
        string? selectedUnitId)
    {
        if (!TryResolveWarpTargetScene(bf, selectedUnitId, out var systemId, out var eventRegionId))
        {
            return null;
        }

        return TacticalSceneBattlefieldService.EnsureSceneBattlefield(state, systemId, eventRegionId).battlefieldId;
    }

    private static bool IsValidFireTarget(BattlefieldUnit? target) =>
        target != null && !target.IsDestroyed() && !BattlefieldSceneProxyService.IsSceneProxy(target);

    public static string OrderRetreat(GameState state, BattlefieldState bf) =>
        HarvestCombatRules.OrderHarvesterRetreat(state, bf);

    // liketocoode3a5

    public static IEnumerable<BattlefieldUnit> ResolveCommandTargets(
        BattlefieldState bf,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds,
        bool allFriendlyIfEmpty = true)
    {
        foreach (var u in ResolveRawCommandTargets(bf, selectedFriendlyUnitIds, allFriendlyIfEmpty))
        {
            if (AcceptsFleetMovementOrder(u))
            {
                yield return u;
            }
        }
    }

    public static IEnumerable<BattlefieldUnit> ResolveFocusTargets(
        BattlefieldState bf,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds,
        bool allFriendlyIfEmpty = true)
    {
        if (selectedFriendlyUnitIds != null && selectedFriendlyUnitIds.Count > 0)
        {
            foreach (var u in bf.units)
            {
                if (u.unitId != null
                    && selectedFriendlyUnitIds.Contains(u.unitId)
                    && u.side == UnitSide.FRIENDLY
                    && !u.IsDestroyed()
                    && !u.isBuilding)
                {
                    yield return u;
                }
            }
            yield break;
        }

        foreach (var u in ResolveRawCommandTargets(bf, selectedFriendlyUnitIds, allFriendlyIfEmpty))
        {
            if (AcceptsFleetMovementOrder(u))
            {
                yield return u;
            }
        }
    }

    private static IEnumerable<BattlefieldUnit> ResolveRawCommandTargets(
        BattlefieldState bf,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds,
        bool allFriendlyIfEmpty)
    {
        if (selectedFriendlyUnitIds != null && selectedFriendlyUnitIds.Count > 0)
        {
            foreach (var u in bf.units)
            {
                if (u.unitId != null
                    && selectedFriendlyUnitIds.Contains(u.unitId)
                    && u.side == UnitSide.FRIENDLY
                    && !u.IsDestroyed()
                    && !u.isBuilding)
                {
                    yield return u;
                }
            }
            yield break;
        }

        if (!allFriendlyIfEmpty)
        {
            yield break;
        }

        foreach (var u in bf.units)
        {
            if (u.side == UnitSide.FRIENDLY && !u.IsDestroyed() && !u.isBuilding)
            {
                yield return u;
            }
        }
    }

    private static bool AcceptsFleetMovementOrder(BattlefieldUnit u) =>
        !u.IsBallisticMissile()
        && (u.parentUnitId == null
            || !("STRIKE_CRAFT".Equals(u.tonnageClass, StringComparison.Ordinal)
                || BoardSummonWingService.WingTonnageClass.Equals(u.tonnageClass, StringComparison.Ordinal)));

    // liketocoode34e

    public static string RallyToBattlefield(
        GameState state,
        BattlefieldState bf,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds = null)
    {
        var possessor = BattlefieldSystem.FindPossessedUnit(state, bf);
        var count = 0;
        foreach (var u in ResolveCommandTargets(bf, selectedFriendlyUnitIds))
        {
            u.aiOrder = UnitAiOrder.RALLY;
            u.rallyPointUnitId = possessor?.unitId;
            count++;
        }
        return count > 0 ? "已向本战场集合 " + count + " 艘" : "无可集合舰";
    }

    // liketocoo3e345

    public static string OrderFollow(
        GameState state,
        BattlefieldState bf,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds = null)
    {
        var possessor = BattlefieldSystem.FindPossessedUnit(state, bf);
        if (possessor == null)
        {
            return "请先附身一艘舰";
        }
        var count = 0;
        foreach (var u in ResolveCommandTargets(bf, selectedFriendlyUnitIds))
        {
            if (ReferenceEquals(u, possessor))
            {
                continue;
            }
            u.aiOrder = UnitAiOrder.FOLLOW;
            count++;
        }
        return count > 0 ? "已下令 " + count + " 艘跟随" : "无其他可跟随的舰";
    }

    public static string OrderFocus(
        GameState state,
        BattlefieldState bf,
        string? targetUnitId,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds = null)
    {
        var possessor = BattlefieldSystem.FindPossessedUnit(state, bf);
        var focusId = targetUnitId ?? possessor?.targetUnitId;
        var targets = ResolveFocusTargets(bf, selectedFriendlyUnitIds).ToList();
        if (focusId != null)
        {
            foreach (var u in targets)
            {
                u.aiOrder = UnitAiOrder.FOCUS;
                u.targetUnitId = focusId;
                u.explicitFocus = true;
            }

            if (possessor != null)
            {
                possessor.targetUnitId = focusId;
                possessor.explicitFocus = true;
            }
        }

        SetAck(targets);
        var wingMsg = StrikeWingOrderService.OrderFocusWings(bf, focusId, selectedFriendlyUnitIds);
        return FormatOrderAck(targets.Count, "集火")
            + (wingMsg.Contains('0') ? "" : "；" + wingMsg);
    }

    public static string OrderStop(
        GameState state,
        BattlefieldState bf,
        bool allFriendly,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds = null)
    {
        var count = 0;
        foreach (var u in ResolveCommandTargets(bf, allFriendly ? null : selectedFriendlyUnitIds))
        {
            if (!allFriendly && state.possessingMemberId != null
                && !state.possessingMemberId.Equals(u.memberId, StringComparison.Ordinal)
                && (selectedFriendlyUnitIds == null || selectedFriendlyUnitIds.Count == 0))
            {
                continue;
            }
            u.aiOrder = UnitAiOrder.STOP;
            u.approachTargetUnitId = null;
            u.orbitTargetUnitId = null;
            u.targetUnitId = null;
            u.explicitFocus = false;
            u.commandMaintainDistM = 0f;
            u.orbitRadiusM = 0f;
            u.orbitPhase = 0f;
            u.approachHeadingTimerSec = 0f;
            u.throttleOn = false;
            u.vx = 0f;
            u.vy = 0f;
            u.vz = 0f;
            count++;
        }
        return allFriendly ? "集体停船 " + count + " 艘" : "停船 " + count + " 艘";
    }

    public static string OrderCeaseFire(
        GameState state,
        BattlefieldState bf,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds = null) =>
        StrikeWingOrderService.OrderCeaseFireWings(bf, selectedFriendlyUnitIds);

    public static string OrderOrbit(
        GameState state,
        BattlefieldState bf,
        string? targetUnitId,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds = null,
        float? rangeKm = null)
    {
        var targets = ResolveCommandTargets(bf, selectedFriendlyUnitIds, allFriendlyIfEmpty: true).ToList();
        if (targetUnitId == null || FindUnit(bf, targetUnitId) == null)
        {
            return FormatOrderAck(0, "环绕");
        }

        var orbitRadiusM = rangeKm.HasValue ? TacticalRangeScale.KmToMeters(rangeKm.Value) : 0f;
        var count = 0;
        foreach (var u in targets)
        {
            u.aiOrder = UnitAiOrder.ORBIT;
            u.orbitTargetUnitId = targetUnitId;
            u.approachTargetUnitId = null;
            u.approachHeadingTimerSec = 0f;
            u.orbitPhase = OrbitEntryResolver.OrbitPhaseSeek;
            u.orbitRadiusM = orbitRadiusM;
            u.explicitFocus = false;
            if (FindUnit(bf, targetUnitId) is { } orbitTarget)
            {
                ShipMotionIntegrator.SnapHeadingToward(u, orbitTarget.x, orbitTarget.y, orbitTarget.z);
            }

            if (u.unitId != null)
            {
                CombatTelemetryLog.LogOrder(u.unitId, "ORBIT→" + targetUnitId);
            }

            count++;
        }

        SetAck(targets.Take(count).ToList());
        return FormatOrderAck(count, "环绕");
    }

    // l1ketocoode345

    public static string OrderApproach(
        GameState state,
        BattlefieldState bf,
        string? targetUnitId,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds = null,
        float? rangeKm = null)
    {
        var targets = ResolveApproachTargets(state, bf, selectedFriendlyUnitIds).ToList();
        var maintainM = rangeKm.HasValue ? TacticalRangeScale.KmToMeters(rangeKm.Value) : 0f;
        if (targetUnitId == null || FindUnit(bf, targetUnitId) == null)
        {
            return FormatOrderAck(0, "接近");
        }

        var count = 0;
        foreach (var u in targets)
        {
            u.aiOrder = UnitAiOrder.APPROACH;
            u.approachTargetUnitId = targetUnitId;
            u.approachHeadingTimerSec = 0f;
            u.commandMaintainDistM = maintainM;
            u.orbitTargetUnitId = null;
            u.explicitFocus = false;
            u.throttleOn = true;
            if (FindUnit(bf, targetUnitId) is { } approachTarget)
            {
                ShipMotionIntegrator.SnapHeadingToward(u, approachTarget.x, approachTarget.y, approachTarget.z);
            }

            if (u.unitId != null)
            {
                CombatTelemetryLog.LogOrder(u.unitId, "APPROACH→" + targetUnitId);
            }

            count++;
        }

        SetAck(targets.Take(count).ToList());
        return FormatOrderAck(count, "接近");
    }

    // liketoco0de345

    public static string OrderAway(
        GameState state,
        BattlefieldState bf,
        string? targetUnitId,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds = null,
        float? rangeKm = null)
    {
        var targets = ResolveApproachTargets(state, bf, selectedFriendlyUnitIds).ToList();
        var maintainM = rangeKm.HasValue ? TacticalRangeScale.KmToMeters(rangeKm.Value) : 0f;
        if (targetUnitId == null || FindUnit(bf, targetUnitId) == null)
        {
            return FormatOrderAck(0, "远离");
        }

        var count = 0;
        foreach (var u in targets)
        {
            u.aiOrder = UnitAiOrder.AWAY;
            u.approachTargetUnitId = targetUnitId;
            u.approachHeadingTimerSec = 0f;
            u.commandMaintainDistM = maintainM;
            u.orbitTargetUnitId = null;
            u.explicitFocus = false;
            u.throttleOn = true;
            if (FindUnit(bf, targetUnitId) is { } awayTarget)
            {
                ShipMotionIntegrator.SnapHeadingAway(u, awayTarget.x, awayTarget.y, awayTarget.z);
            }

            if (u.unitId != null)
            {
                CombatTelemetryLog.LogOrder(u.unitId, "AWAY→" + targetUnitId);
            }

            count++;
        }

        SetAck(targets.Take(count).ToList());
        return FormatOrderAck(count, "远离");
    }

    // lik3tocoode345

    private static IEnumerable<BattlefieldUnit> ResolveApproachTargets(
        GameState state,
        BattlefieldState bf,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds) =>
        ResolveCommandTargets(bf, selectedFriendlyUnitIds, allFriendlyIfEmpty: true);

    private static BattlefieldUnit? FindUnit(BattlefieldState bf, string unitId)
    {
        foreach (var u in bf.units)
        {
            if (unitId.Equals(u.unitId, StringComparison.Ordinal))
            {
                return u;
            }
        }

        return null;
    }

    // liketocoode3e5

    public static string OrderFollowAttack(
        GameState state,
        BattlefieldState bf,
        string? targetUnitId,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds = null)
    {
        var possessor = BattlefieldSystem.FindPossessedUnit(state, bf);
        var focusId = targetUnitId ?? possessor?.targetUnitId;
        var targets = ResolveCommandTargets(bf, selectedFriendlyUnitIds).ToList();
        var count = 0;
        foreach (var u in targets)
        {
            if (possessor != null && ReferenceEquals(u, possessor))
            {
                continue;
            }

            if (focusId == null)
            {
                continue;
            }

            u.aiOrder = UnitAiOrder.FOLLOW_ATTACK;
            u.targetUnitId = focusId;
            u.explicitFocus = true;
            count++;
        }

        if (possessor != null && focusId != null)
        {
            possessor.targetUnitId = focusId;
            possessor.explicitFocus = true;
        }

        return FormatOrderAck(count, "跟随攻击");
    }

    public static string OrderScatter(
        GameState state,
        BattlefieldState bf,
        Random rng,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds = null)
    {
        var count = 0;
        foreach (var u in ResolveCommandTargets(bf, selectedFriendlyUnitIds))
        {
            if (u.inTacticalWarp || u.pinnedToBattlefield)
            {
                continue;
            }
            u.aiOrder = UnitAiOrder.SCATTER;
            u.facingRad = (float)(rng.NextDouble() * Math.PI * 2);
            u.pitchRad = (float)(rng.NextDouble() * 0.4 - 0.2);
            u.throttleOn = true;
            u.explicitFocus = false;
            u.targetUnitId = null;
            count++;
        }
        return count > 0 ? "已下令 " + count + " 艘散开" : "无可散开舰";
    }

    public static string OrderWarp(
        GameState state,
        BattlefieldState bf,
        string targetBattlefieldId,
        ShipRegistry ships,
        bool allFriendly,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds = null,
        float? landingKm = null)
    {
        var target = TacticalWarpService.FindBattlefield(state, targetBattlefieldId);
        if (target == null || target.finished)
        {
            return FormatOrderAck(0, "跃迁");
        }

        if (target.battlefieldId != null
            && target.battlefieldId.Equals(bf.battlefieldId, StringComparison.Ordinal))
        {
            return FormatOrderAck(0, "跃迁");
        }

        if (bf.systemId != null && target.systemId != null
            && !bf.systemId.Equals(target.systemId, StringComparison.Ordinal))
        {
            return FormatOrderAck(0, "跃迁");
        }

        if (landingKm.HasValue)
        {
            state.tacticalWarpLandingDistM = TacticalWarpLandingService.ClampLandingDistM(
                TacticalRangeScale.KmToMeters(landingKm.Value));
        }

        var count = 0;
        foreach (var u in ResolveCommandTargets(bf, allFriendly ? null : selectedFriendlyUnitIds))
        {
            if (!allFriendly && state.possessingMemberId != null
                && !state.possessingMemberId.Equals(u.memberId, StringComparison.Ordinal)
                && (selectedFriendlyUnitIds == null || selectedFriendlyUnitIds.Count == 0))
            {
                continue;
            }

            if (u.inTacticalWarp || u.pinnedToBattlefield)
            {
                continue;
            }

            PrepareUnitForWarp(u);

            var unitLanding = landingKm.HasValue
                ? TacticalRangeScale.KmToMeters(landingKm.Value)
                : u.warpLandingDistM >= TacticalWarpLandingService.MinLandingDistM
                    ? u.warpLandingDistM
                    : TacticalWarpLandingService.ResolveLandingDistM(state);
            var err = TacticalWarpService.TryBeginWarp(
                state,
                u,
                bf,
                target,
                hull: u.hullId != null ? ships.FindHull(u.hullId) : null,
                unitLanding);
            if (err == null)
            {
                count++;
            }
        }

        return FormatOrderAck(count, "跃迁");
    }

    private static void PrepareUnitForWarp(BattlefieldUnit u)
    {
        if (u.SpeedMps() <= TacticalWarpService.MaxInitiateWarpSpeedMps)
        {
            return;
        }

        u.vx = 0f;
        u.vy = 0f;
        u.vz = 0f;
        u.throttleOn = false;
    }

    public static string OrderEnterBuilding(
        GameState state,
        BattlefieldState bf,
        string? targetUnitId,
        IReadOnlyCollection<string>? selectedFriendlyUnitIds = null)
    {
        if (targetUnitId == null)
        {
            return FormatOrderAck(0, "进入建筑");
        }

        var gate = FindUnit(bf, targetUnitId);
        if (!JumpBridgeUnitService.IsJumpBridgeBuilding(gate))
        {
            return FormatOrderAck(0, "进入建筑");
        }

        var count = 0;
        foreach (var u in ResolveCommandTargets(bf, selectedFriendlyUnitIds))
        {
            if (JumpBridgeTransitService.TryTransit(state, u, bf, gate!, out _))
            {
                count++;
            }
        }

        return FormatOrderAck(count, "进入建筑");
    }

    // liket0coode345

    public static void RallySide(BattlefieldState bf, UnitSide side, BattlefieldUnit anchor)
    {
        foreach (var u in bf.units)
        {
            if (u.side != side || u.IsDestroyed() || u.isBuilding)
            {
                continue;
            }

            if (u.aiOrder is UnitAiOrder.STOP or UnitAiOrder.MANUAL or UnitAiOrder.RECALL)
            {
                continue;
            }

            u.aiOrder = UnitAiOrder.RALLY;
            u.rallyPointUnitId = anchor.unitId;
        }
    }
}
