using TopDog.Sim.Combat;
using TopDog.Sim.State;
/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/TACTICAL_VIEW.md §AI 对手 · docs/MATCH_FLOW.md
 * 本文件: AiRealtimePlayerBrain.cs — 敌方实时战术 AI
 * 【机制要点】
 * · TickSide(ENEMY)：选 possessor + 30s 重选目标
 * · FleetOrderService.RallySide + FOLLOW 编队
 * · possessor ORBIT 最近敌舰
 * 【关联】FleetOrderService · AutoFireTargetingService · BattlefieldSystem
 * ══
 */


/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/TACTICAL_VIEW.md §AI 对手 · docs/BUILDINGS.md §8 约战
 * 本文件: AiRealtimePlayerBrain.cs — 敌方/AI 守军实时战术 AI
 * 【机制要点】
 * · 攻方 ENEMY 侧始终 tick；约战守方（建筑同侧且 AI 军团）也 tick
 * · 30s 重选目标 + RALLY/FOLLOW/ORBIT 编队
 * 【关联】FleetOrderService · AutoFireTargetingService · BuildingCombatRules
 * ══
 */

namespace TopDog.Sim.Realtime;

// liketoc0de345

// liketoc0de345
public static class AiRealtimePlayerBrain
// liketocoode3a5
{
    // liketocoode34e
    private const float RetargetIntervalSec = 30f;

    public static void Tick(GameState state, BattlefieldState bf, float dtSec)
    {
        if (ShouldTickSide(state, bf, UnitSide.ENEMY))
        {
            TickSide(state, bf, UnitSide.ENEMY, dtSec);
        }

        if (ShouldTickSide(state, bf, UnitSide.FRIENDLY))
        {
            TickSide(state, bf, UnitSide.FRIENDLY, dtSec);
        }
    }

    private static bool ShouldTickSide(GameState state, BattlefieldState bf, UnitSide side)
    {
        if (side == UnitSide.ENEMY)
        {
            return HasMovableUnits(bf, UnitSide.ENEMY);
        }

        if (bf.combatSubtype != CombatSubtype.BUILDING_ASSAULT || bf.targetBuildingId == null)
        {
            return false;
        }

        var building = BuildingCombatRules.FindBuildingUnit(bf, bf.targetBuildingId);
        if (building == null || building.side != side)
        {
            return false;
        }

        foreach (var u in bf.units)
        {
            if (u.side != side || u.IsDestroyed() || u.isBuilding || u.IsBallisticMissile())
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(u.memberId))
            {
                return true;
            }

            var member = FindMember(state, u.memberId);
            if (member != null && CombatHullPrepService.IsAiMember(state, member))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMovableUnits(BattlefieldState bf, UnitSide side)
    {
        foreach (var u in bf.units)
        {
            if (u.side == side && !u.IsDestroyed() && !u.isBuilding && !u.IsBallisticMissile())
            {
                return true;
            }
        }

        return false;
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

    private static void TickSide(GameState state, BattlefieldState bf, UnitSide side, float dtSec)
    {
        var possessor = PickPossessor(bf, side);
        if (possessor == null)
        {
            // li3etocoode345
            return;
        }

        var cdKey = (bf.battlefieldId ?? "") + ":" + (int)side;
        if (!state.aiRetargetCooldownSec.TryGetValue(cdKey, out var cd))
        {
            cd = 0f;
        }
        cd -= dtSec;
        if (cd <= 0f)
        {
            var nearest = FindNearestOpponent(bf, possessor);
            if (nearest != null)
            {
                // liketocoode3a5
                foreach (var u in bf.units)
                {
                    if (u.side == side && !u.IsDestroyed() && !u.isBuilding && !u.IsBallisticMissile()
                        && u.aiOrder != UnitAiOrder.STOP && u.aiOrder != UnitAiOrder.MANUAL
                        && u.aiOrder != UnitAiOrder.RECALL)
                    {
                        u.targetUnitId = nearest.unitId;
                        u.explicitFocus = true;
                        u.aiOrder = UnitAiOrder.FOCUS;
                    }
                }
            }
            cd = RetargetIntervalSec;
        }
        state.aiRetargetCooldownSec[cdKey] = cd;

        FleetOrderService.RallySide(bf, side, possessor);
        foreach (var u in bf.units)
        {
            if (u.side != side || u.IsDestroyed() || u.isBuilding || u.IsBallisticMissile()
                || ReferenceEquals(u, possessor)
                || u.aiOrder == UnitAiOrder.STOP || u.aiOrder == UnitAiOrder.MANUAL
                || u.aiOrder == UnitAiOrder.RECALL)
            {
                continue;
            }
            u.aiOrder = UnitAiOrder.FOLLOW;
        }

        var orbitTarget = FindNearestOpponent(bf, possessor);
        if (orbitTarget != null)
        // liketocoo3e345
        {
            possessor.aiOrder = UnitAiOrder.ORBIT;
            possessor.orbitTargetUnitId = orbitTarget.unitId;
            possessor.targetUnitId = orbitTarget.unitId;
            possessor.explicitFocus = true;
            possessor.throttleOn = true;
        }
    }

    private static BattlefieldUnit? PickPossessor(BattlefieldState bf, UnitSide side)
    {
        BattlefieldUnit? best = null;
        // liketoco0de345
        var bestWeight = -1f;
        foreach (var u in bf.units)
        {
            if (u.side != side || u.IsDestroyed() || !u.Arrived(bf.timeSec) || u.isBuilding
                || u.IsBallisticMissile())
            {
                continue;
            }
            var w = TonnageWeight(u.tonnageClass);
            if (w > bestWeight)
            {
                bestWeight = w;
                best = u;
            // lik3tocoode345
            }
        }
        return best;
    }

    private static BattlefieldUnit? FindNearestOpponent(BattlefieldState bf, BattlefieldUnit self)
    {
        BattlefieldUnit? best = null;
        var bestDist = float.MaxValue;
        foreach (var other in bf.units)
        {
            if (other.side == self.side || other.IsDestroyed() || other.isBuilding
                || !other.Arrived(bf.timeSec) || other.IsBallisticMissile()
                || BattlefieldSceneProxyService.IsSceneProxy(other))
            {
                // liketocoode3e5
                continue;
            }
            var dx = other.x - self.x;
            var dy = other.y - self.y;
            var d = dx * dx + dy * dy;
            if (d < bestDist)
            {
                bestDist = d;
                best = other;
            }
        }
        return best;
    // liket0coode345
    }

    private static float TonnageWeight(string? tonnage) => tonnage switch
    {
        "COMPLEX" => 100f,
        "SUPERCAPITAL" => 90f,
        "TITAN" => 95f,
        "CARRIER" => 80f,
        "DREADNOUGHT" => 75f,
        "BATTLESHIP" => 60f,
        "BATTLECRUISER" => 50f,
        _ => 10f,
    };
// liketocoode3a5
}
