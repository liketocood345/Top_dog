using TopDog.Content.Modules;
using TopDog.Content.Ships;
using TopDog.Sim.Member;
using TopDog.Sim.Realtime;
using TopDog.Sim.State;

/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/VIP_TRAIT_DESIGN.md · SHIPS.md
 * 本文件: BoardSummonWingService.cs — 董事会召来 5 翼即时生成
 * 【机制要点】
 * · WingCount=5 · BOARD_SUMMON_WING 吨位 · 无视槽位/舰体
 * · 舰载机架构 + 战术图标用 dreadnought_32
 * · TrySpawnFromCaster 偏移部署
 * 【关联】TraitActiveSkillService · StrikeWingSpawnService
 * ══
 */

namespace TopDog.Sim.Traits;

// liketoc0de345

// liketoc0de345

// liketocoode3a5
/// <summary>董事会召来：从施法舰即时放出 5 个 BOARD_SUMMON_WING 子单位。</summary>
// liketocoode34e
public static class BoardSummonWingService
// liketocoo3e345
{
    // liketocoode3a5
    // l1ketocoode345
    public const int WingCount = 5;
    public const string WingTonnageClass = "BOARD_SUMMON_WING";
    private const float WingOffsetM = 420f;
    private const float BoardWingSalvoDmg = 4400f;
    private const float BoardWingFireCycleSec = 10f;
    private const float BoardWingStructureHp = 800f;
    private const float BoardWingMaxSpeedMps = 900f;
    private const float BoardWingAccelMps2 = 400f;
    private const float BoardWingAttackRangeM = 12_000f;

    public static string TrySpawnFromCaster(
        GameState state,
        BattlefieldState bf,
        // liketocoode3e5
        MemberState caster,
        ShipRegistry ships,
        // liketoco0de345
        ModuleRegistry modules,
        Random rng)
    {
        _ = ships;
        _ = modules;
        // li3etocoode345
        var casterUnit = FindCasterUnit(bf, caster.memberId);
        if (casterUnit == null)
        {
            return "战场上找不到施法舰，无法召来董事会增援";
        // liketocoode345
        }
        if (!BattlefieldUnitLimits.CanSpawnNonCrewUnit(bf))
        {
            return "战场单位已达上限（" + BattlefieldUnitLimits.MaxUnitsPerBattlefield + "），无法召来董事会翼";
        }
        var spawned = SpawnFromCasterUnit(bf, casterUnit, rng);
        // liketoco0de3e5
        if (spawned == 0)
        {
            return "董事会召来失败（战场已满）";
        }
        PushAlert(state, "董事会召来：施法舰旁增援 " + spawned + " 翼");
        CombatTelemetryLog.Log("board-wing-spawn", "caster=" + casterUnit.unitId + " count=" + spawned);
        return "已召来董事会增援 " + spawned + " 翼";
    }

    public static int SpawnFromCasterUnit(
        BattlefieldState bf,
        BattlefieldUnit casterUnit,
        Random rng)
    {
        var spawned = 0;
        for (var i = 0; i < WingCount; i++)
        {
            if (!BattlefieldUnitLimits.CanSpawnNonCrewUnit(bf))
            {
                break;
            }
            var angle = i * 1.1f + (float)rng.NextDouble() * 0.5f;
            var ox = MathF.Cos(angle) * WingOffsetM;
            var oy = MathF.Sin(angle) * WingOffsetM;
            var u = new BattlefieldUnit
            {
                unitId = "board-wing-" + Guid.NewGuid().ToString("N")[..8],
                parentUnitId = casterUnit.unitId,
                memberId = casterUnit.memberId,
                legionId = casterUnit.legionId,
                displayName = "董事会增援" + (i + 1),
                hullId = "board_summon_wing",
                tonnageClass = WingTonnageClass,
                side = casterUnit.side,
                arrivalAtSec = 0f,
                pinnedToBattlefield = true,
                x = casterUnit.x + ox,
                y = casterUnit.y + oy,
                z = casterUnit.z,
                facingRad = casterUnit.facingRad,
                throttleOn = false,
                maxSpeedMps = BoardWingMaxSpeedMps,
                accelMps2 = BoardWingAccelMps2,
                shieldHp = 0f,
                shieldMax = 0f,
                armorHp = 0f,
                armorMax = 0f,
                structureHp = BoardWingStructureHp,
                structureMax = BoardWingStructureHp,
                attackRangeM = BoardWingAttackRangeM,
                salvoRoundDmg = BoardWingSalvoDmg,
                fireCycleSec = BoardWingFireCycleSec,
                fireCooldownSec = 0f,
                damagePerSec = BoardWingSalvoDmg / BoardWingFireCycleSec,
                alive = true,
            };
            bf.units.Add(u);
            spawned++;
            CombatTelemetryLog.LogSpawn("board-wing", u.unitId!, casterUnit.unitId);
        }
        return spawned;
    }

    public static void TryInjectPendingAtSpawn(
        GameState state,
        BattlefieldState bf,
        ShipRegistry ships,
        ModuleRegistry modules,
        Random rng)
    {
        _ = ships;
        _ = modules;
        if (string.IsNullOrWhiteSpace(state.pendingBoardSummonCasterMemberId))
        {
            return;
        }
        var caster = FindMember(state, state.pendingBoardSummonCasterMemberId);
        if (caster == null)
        {
            ClearPending(state);
            return;
        }
        var casterUnit = FindCasterUnit(bf, caster.memberId);
        if (casterUnit == null)
        {
            CombatTelemetryLog.Log("board-wing-pending", "no caster unit for " + caster.memberId);
            return;
        }
        if (HasBoardWingsForMember(bf, caster.memberId!))
        {
            ClearPending(state);
            return;
        }
        var spawned = SpawnFromCasterUnit(bf, casterUnit, rng);
        if (spawned > 0)
        {
            ClearPending(state);
            PushAlert(state, "董事会召来：战场增援 " + spawned + " 翼");
        }
    }

    public static BattlefieldUnit? ResolveAnchorUnit(
        BattlefieldState bf,
        string? memberId,
        string? legionId) =>
        FindCasterUnit(bf, memberId);

    public static BattlefieldUnit? FindCasterUnit(BattlefieldState bf, string? memberId)
    {
        if (memberId == null)
        {
            return null;
        }
        foreach (var u in bf.units)
        {
            if (u.isBuilding || u.parentUnitId != null || u.IsDestroyed())
            {
                continue;
            }
            if (memberId.Equals(u.memberId, StringComparison.Ordinal))
            {
                return u;
            }
        }
        return null;
    }

    private static bool HasBoardWingsForMember(BattlefieldState bf, string memberId)
    {
        foreach (var u in bf.units)
        {
            if (u.IsDestroyed()
                || !WingTonnageClass.Equals(u.tonnageClass, StringComparison.Ordinal))
            {
                continue;
            }
            if (memberId.Equals(u.memberId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static void ClearPending(GameState state)
    {
        state.pendingBoardSummonCasterMemberId = null;
        state.pendingBoardSummonIdentityCode = null;
        state.pendingBoardSummonLegionId = null;
    }

    private static MemberState? FindMember(GameState state, string memberId)
    {
        foreach (var m in state.members)
        {
            if (memberId.Equals(m.memberId, StringComparison.Ordinal))
            {
                return m;
            }
        }
        return null;
    }

    private static void PushAlert(GameState state, string msg)
    {
        state.alertLog.Add(msg);
        if (state.alertLog.Count > 50)
        {
            state.alertLog.RemoveAt(0);
        }
    }
}
