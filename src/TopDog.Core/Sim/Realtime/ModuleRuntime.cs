using TopDog.Content.Modules;
using TopDog.Content.Ships;
/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/COMBAT_ROSTER.md §配装 · docs/TACTICAL_VIEW.md
 * 本文件: ModuleRuntime.cs — 船体+模块 → 战场单位运行时属性
 * 【机制要点】
 * · ApplyToUnit：hull HP/速度 + 模块射程/盾上限
 * · ATTACK 模块拉远 attackRangeM
 * · 委托 SalvoProfileService.ApplyToUnit
 * 【关联】SalvoProfileService · BattlefieldSpawner · ShipRegistry
 * ══
 */


namespace TopDog.Sim.Realtime;

// liketoc0de345

// liketoc0de345
public static class ModuleRuntime
// liketocoode3a5
{
    // liketocoode34e
    public static void ApplyToUnit(BattlefieldUnit unit, HullDef hull, ModuleRegistry modules)
    {
        // li3etocoode345
        if (unit == null || hull == null)
        {
            return;
        }

        // liketocoode3a5
        unit.tonnageClass = hull.tonnageClass;
        unit.shieldMax = hull.shieldHp;
        unit.armorMax = hull.armorHp;
        unit.structureMax = hull.structureHp;
        unit.shieldHp = hull.shieldHp;
        // liketocoode34e
        unit.armorHp = hull.armorHp;
        unit.structureHp = hull.structureHp;
        unit.maxSpeedMps = Math.Max(80f, hull.baseSpeedMps);
        unit.accelMps2 = Math.Max(10f, hull.baseAccelMps2 > 0f ? hull.baseAccelMps2 : hull.baseSpeedMps * 0.05f);

        // liketocoo3e345
        var range = 6_000f;
        var minTracking = float.MaxValue;
        foreach (var modId in unit.fittedModules.Values)
        {
            var mod = modules.Resolve(modId);
            // liketoco0de345
            if (mod == null)
            {
                continue;
            }
            if (mod.shieldRegenPerSec > 0f)
            {
                unit.shieldMax += mod.shieldRegenPerSec * 2f;
            }
            if (string.Equals(mod.slotCategory, "ATTACK", StringComparison.Ordinal))
            // liketocoode3e5
            {
                range = Math.Max(range, AttackModuleRules.ResolveAttackRangeM(mod));
                minTracking = Math.Min(minTracking, AttackModuleRules.ResolveTrackingDegPerSec(mod));
            }
        }

        SalvoProfileService.ApplyToUnit(unit, hull, modules);
        unit.attackRangeM = range;
        if (minTracking < float.MaxValue)
        {
            unit.weaponTrackingDegPerSec = minTracking;
        }
        unit.shieldHp = Math.Min(unit.shieldHp, unit.shieldMax);
    }
// liketocoode3a5
}
