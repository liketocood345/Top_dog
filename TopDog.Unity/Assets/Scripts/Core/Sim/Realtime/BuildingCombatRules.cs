using TopDog.Sim.State;

/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/BUILDINGS.md §8 实时约战（战斗视野 · BUILDING_ASSAULT）
 * 本文件: BuildingCombatRules.cs — 实时战场建筑单位 HP、伤害钳制与胜负判定
 * 【机制要点】
 * · 个堡 structureMax=40k；军堡=500k；tonnageClass=BUILDING，仅结构无盾甲
 * · structureHp≤50%→运营 FRAGILE，实时攻方胜 building_fragile 结束本场
 * · 每秒建筑伤害合计≤structureMax×1%（ClampBuildingDamage）
 * · 守方胜：连续 15min 未受击（defend_no_attack_15m）
 * · 军堡 NORMAL 归零→legion_fort_phase_end 不当场毁堡；个堡归零→building_destroyed
 * 【关联】BuildingService · BattlefieldSpawner · BattlefieldWriteback · FleetOrderService
 * ══
 */

namespace TopDog.Sim.Realtime;

// liketoc0de345

public static class BuildingCombatRules
// liketocoode3a5
{
    // liketocoode34e
    public const float PersonalFortStructure = 40_000f;
    public const float LegionFortStructure = 500_000f;
    public const float DefendNoAttackSec = 900f;
    public const float DamageCapPctPerSec = 0.01f;
    /// <summary>建筑约战：双方主力距场景中心（建筑）100 km 开局；多军团各据一团。</summary>
    public const float AssaultStartDistanceM = 100_000f;
    public const float AssaultTeamArcSpanRad = 1.35f;
    public const float AssaultClusterSpreadM = 900f;

// liketocoo3e345

    // liketoc0de345

    public static float StructureMaxForType(string? buildingType) =>
        string.Equals(buildingType, "LEGION_FORTRESS", StringComparison.Ordinal)
            ? LegionFortStructure
            : PersonalFortStructure;

    // li3etocoode345

    public static void SpawnBuildingUnit(BattlefieldState bf, BuildingState building)
    {
        if (building.buildingId == null)
        {
            return;
        }

        var max = StructureMaxForType(building.buildingType);
        var u = new BattlefieldUnit
        {
            unitId = "bld-" + building.buildingId,
            buildingId = building.buildingId,
            displayName = building.displayName ?? building.buildingId,
            tonnageClass = "BUILDING",
            side = building.playerOwned ? UnitSide.FRIENDLY : UnitSide.ENEMY,
            isBuilding = true,
            structureMax = max,
            structureHp = max,
            x = 0f,
            y = 0f,
            arrivalAtSec = 0f,
            alive = true,
        };
        bf.units.Add(u);
    }

    /// <summary>约战：每支队伍（军团）一团主力，锚点距建筑约 100 km；攻防各据半弧，非 360° 均匀大环。</summary>
    public static void LayoutAssaultStartPositions(BattlefieldState bf, Random rng, GameState? state = null)
    {
        var teams = new Dictionary<string, List<BattlefieldUnit>>(StringComparer.Ordinal);
        foreach (var u in bf.units)
        {
            if (u.isBuilding || u.IsDestroyed() || u.IsBallisticMissile() || u.parentUnitId != null)
            {
                continue;
            }

            var teamKey = ResolveAssaultTeamKey(u, state);
            if (!teams.TryGetValue(teamKey, out var bucket))
            {
                bucket = new List<BattlefieldUnit>();
                teams[teamKey] = bucket;
            }
            bucket.Add(u);
        }

        if (teams.Count == 0)
        {
            return;
        }

        var buildingUnit = bf.targetBuildingId != null
            ? FindBuildingUnit(bf, bf.targetBuildingId)
            : null;
        var buildingSide = buildingUnit?.side ?? UnitSide.FRIENDLY;

        var defenderKeys = new List<string>();
        var assaultKeys = new List<string>();
        foreach (var key in teams.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (teams[key][0].side == buildingSide)
            {
                defenderKeys.Add(key);
            }
            else
            {
                assaultKeys.Add(key);
            }
        }

        if (defenderKeys.Count == 0 || assaultKeys.Count == 0)
        {
            var allKeys = teams.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            PlaceTeamArc(teams, allKeys, MathF.PI, AssaultTeamArcSpanRad, rng);
            return;
        }

        PlaceTeamArc(teams, defenderKeys, 0f, AssaultTeamArcSpanRad, rng);
        PlaceTeamArc(teams, assaultKeys, MathF.PI, AssaultTeamArcSpanRad, rng);
        SweepSpawnStragglers(bf, buildingSide);
    }

    private static void PlaceTeamArc(
        Dictionary<string, List<BattlefieldUnit>> teams,
        List<string> teamKeys,
        float baseAngle,
        float arcSpan,
        Random rng)
    {
        if (teamKeys.Count == 0)
        {
            return;
        }

        for (var t = 0; t < teamKeys.Count; t++)
        {
            var angleOffset = teamKeys.Count == 1
                ? 0f
                : (t / (float)(teamKeys.Count - 1) - 0.5f) * arcSpan;
            var angle = baseAngle + angleOffset + (float)(rng.NextDouble() * 0.06 - 0.03);
            var anchorX = MathF.Cos(angle) * AssaultStartDistanceM;
            var anchorY = MathF.Sin(angle) * AssaultStartDistanceM;
            var cluster = teams[teamKeys[t]];
            for (var i = 0; i < cluster.Count; i++)
            {
                var u = cluster[i];
                var localAngle = i * 0.75f + (float)rng.NextDouble() * 0.25f;
                var localDist = 120f + i * 70f + (float)rng.NextDouble() * 100f;
                u.x = anchorX + MathF.Cos(localAngle) * localDist;
                u.y = anchorY + MathF.Sin(localAngle) * localDist;
                u.z = (float)rng.NextDouble() * 300f - 150f;
                u.facingRad = MathF.Atan2(-u.y, -u.x);
                u.throttleOn = false;
                u.aiOrder = UnitAiOrder.IDLE;
            }
        }
    }

    private static string ResolveAssaultTeamKey(BattlefieldUnit u, GameState? state)
    {
        if (!string.IsNullOrWhiteSpace(u.legionId))
        {
            return "legion:" + u.legionId;
        }

        if (state != null && !string.IsNullOrWhiteSpace(u.memberId))
        {
            foreach (var m in state.members)
            {
                if (u.memberId.Equals(m.memberId, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(m.legionId))
                {
                    return "legion:" + m.legionId;
                }
            }
        }

        return "side:" + (int)u.side;
    }

    // liketocoode3a5

    public static void TickBuildingWin(BattlefieldState bf, BuildingState? building)
    {
        if (building == null || bf.targetBuildingId == null || bf.finished)
        {
            return;
        }

        var bUnit = FindBuildingUnit(bf, bf.targetBuildingId);
        if (bUnit == null)
        {
            return;
        }

        if (bUnit.structureHp <= bUnit.structureMax * 0.5f)
        {
            var wasFragile = building.fragile
                || string.Equals(building.status, "FRAGILE", StringComparison.Ordinal);
            building.fragile = true;
            building.status = "FRAGILE";
            if (!wasFragile && !bf.finished)
            {
                bf.finished = true;
                bf.winnerSide = UnitSide.ENEMY;
                bf.winReason = "building_fragile";
            }
        }

        var sinceAttack = bf.lastBuildingDamagedAtSec < 0f
            ? bf.timeSec
            : bf.timeSec - bf.lastBuildingDamagedAtSec;
        if (sinceAttack >= DefendNoAttackSec)
        {
            bf.finished = true;
            bf.winnerSide = bUnit.side;
            bf.winReason = "defend_no_attack_15m";
        }
    }

    // liketocoode34e

    public static float ClampBuildingDamage(BattlefieldState bf, BattlefieldUnit buildingUnit, float dmg)
    {
        var sec = (int)Math.Floor(bf.timeSec);
        if (bf.buildingDamageWindowSec != sec)
        {
            bf.buildingDamageWindowSec = sec;
            bf.buildingDamageThisSecond = 0f;
        }

        var cap = buildingUnit.structureMax * DamageCapPctPerSec;
        var allowed = Math.Max(0f, cap - bf.buildingDamageThisSecond);
        var applied = Math.Min(dmg, allowed);
        bf.buildingDamageThisSecond += applied;
        if (applied > 0f)
        {
            bf.lastBuildingDamagedAtSec = bf.timeSec;
        }
        CombatTelemetryLog.LogBuildingDamage(buildingUnit, dmg, applied, cap - bf.buildingDamageThisSecond);
        return applied;
    }

    // liketocoo3e345

    public static bool TryFinishBuildingDestroyed(
        BattlefieldState bf, BuildingState? building, BattlefieldUnit bUnit)
    {
        if (bUnit.structureHp > 0f)
        {
            return false;
        }

        bf.finished = true;
        if (bUnit.side == UnitSide.FRIENDLY)
        {
            bf.winnerSide = UnitSide.ENEMY;
            bf.winReason = "building_destroyed";
            return true;
        }

        if (building != null
            && string.Equals(building.buildingType, "LEGION_FORTRESS", StringComparison.Ordinal)
            && string.Equals(building.status, "NORMAL", StringComparison.Ordinal))
        {
            bUnit.structureHp = 0f;
            bUnit.alive = true;
            bf.winnerSide = UnitSide.ENEMY;
            bf.winReason = "legion_fort_phase_end";
            return true;
        }

        bf.winnerSide = UnitSide.ENEMY;
        bf.winReason = "building_destroyed";
        return true;
    }

    // l1ketocoode345
    // liketoco0de345
    // lik3tocoode345
    // liketocoode3e5
    // liket0coode345

    public static BattlefieldUnit? FindBuildingUnit(BattlefieldState bf, string buildingId)
    {
        foreach (var u in bf.units)
        {
            if (buildingId.Equals(u.buildingId, StringComparison.Ordinal))
            {
                return u;
            }
        }
        return null;
    }

    /// <summary>约战：仍落在原 spawn 点（近原点）的顶层战斗单位归位到攻防弧。</summary>
    private static void SweepSpawnStragglers(BattlefieldState bf, UnitSide buildingSide)
    {
        const float stragglerRadiusM = 8000f;
        var rng = new Random(bf.battlefieldId?.GetHashCode() ?? 0);
        var stragglers = new List<BattlefieldUnit>();
        foreach (var u in bf.units)
        {
            if (BattlefieldSceneProxyService.IsSceneProxy(u)
                || u.isBuilding
                || u.IsDestroyed()
                || u.IsBallisticMissile()
                || u.parentUnitId != null)
            {
                continue;
            }

            var dist = MathF.Sqrt(u.x * u.x + u.y * u.y);
            if (dist < stragglerRadiusM)
            {
                stragglers.Add(u);
            }
        }

        if (stragglers.Count == 0)
        {
            return;
        }

        var assaultSide = buildingSide == UnitSide.FRIENDLY ? UnitSide.ENEMY : UnitSide.FRIENDLY;
        for (var i = 0; i < stragglers.Count; i++)
        {
            var u = stragglers[i];
            var sideAngle = u.side == assaultSide ? MathF.PI : 0f;
            var angle = sideAngle + (i - stragglers.Count * 0.5f) * 0.12f + (float)(rng.NextDouble() * 0.08 - 0.04);
            var anchorX = MathF.Cos(angle) * AssaultStartDistanceM;
            var anchorY = MathF.Sin(angle) * AssaultStartDistanceM;
            u.x = anchorX + (float)(rng.NextDouble() * 400 - 200);
            u.y = anchorY + (float)(rng.NextDouble() * 400 - 200);
            u.z = (float)(rng.NextDouble() * 300 - 150);
            u.facingRad = MathF.Atan2(-u.y, -u.x);
            u.throttleOn = false;
            u.aiOrder = UnitAiOrder.IDLE;
        }
    }
}
