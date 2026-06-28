using TopDog.Content.Modules;
using TopDog.Content.Ships;
using TopDog.Sim.Member;
using TopDog.Sim.Realtime;
using TopDog.Sim.State;
using TopDog.Sim.Traits;

namespace TopDog.Core.Tests;

[TestFixture]
public sealed class BoardSummonWingTests
{
    [Test]
    public void LiveCombat_Summon_SpawnsFiveWingsFromCaster()
    {
        var state = new GameState
        {
            storyRound = 2,
            phase = GamePhase.COMBAT,
            combatRealtimeActive = true,
        };
        state.legions.Add(new LegionState { legionId = "VIP", isLocal = true });
        state.identities["10001001"] = new IdentityState
        {
            identityCode = "10001001",
            traitIds = { TraitActiveSkillService.BoardSummonTraitId },
        };
        var caster = new MemberState
        {
            memberId = "1000100101",
            identityCode = "10001001",
            legionId = "VIP",
            equippedHullId = "hull_bc_spear",
            traitIds = { TraitActiveSkillService.BoardSummonTraitId },
        };
        state.members.Add(caster);
        IdentityMigrationService.EnsureFromMembers(state);

        var bf = new BattlefieldState
        {
            battlefieldId = "bf-live",
            systemId = "sys1",
            timeSec = 10f,
        };
        var casterUnit = new BattlefieldUnit
        {
            unitId = "u-caster",
            memberId = caster.memberId,
            displayName = "施法舰",
            side = UnitSide.FRIENDLY,
            arrivalAtSec = 0f,
            structureHp = 1000f,
            structureMax = 1000f,
        };
        bf.units.Add(casterUnit);
        state.battlefields.Add(bf);
        state.activeBattlefieldId = bf.battlefieldId;

        var ships = ShipRegistry.LoadDefault();
        var modules = ModuleRegistry.LoadDefault();
        Assert.That(ships, Is.Not.Null);
        Assert.That(modules, Is.Not.Null);

        var echo = BoardSummonWingService.TrySpawnFromCaster(
            state, bf, caster, ships, modules, new Random(1));
        Assert.That(echo, Does.Contain("翼"));

        var wings = bf.units
            .Where(u => BoardSummonWingService.WingTonnageClass.Equals(u.tonnageClass, StringComparison.Ordinal))
            .ToList();
        Assert.That(wings, Has.Count.EqualTo(BoardSummonWingService.WingCount));
        foreach (var w in wings)
        {
            Assert.That(w.parentUnitId, Is.EqualTo(casterUnit.unitId));
            Assert.That(w.Arrived(bf.timeSec), Is.True);
            Assert.That(w.pinnedToBattlefield, Is.True);
            Assert.That(w.salvoRoundDmg, Is.GreaterThan(0f));
        }
    }

    [Test]
    public void CapFull_RejectsSpawn()
    {
        var bf = new BattlefieldState();
        for (var i = 0; i < BattlefieldUnitLimits.MaxUnitsPerBattlefield; i++)
        {
            bf.units.Add(new BattlefieldUnit { unitId = "u-" + i, structureHp = 1f, structureMax = 1f });
        }
        var caster = new BattlefieldUnit
        {
            unitId = "caster",
            memberId = "m1",
            side = UnitSide.FRIENDLY,
            structureHp = 100f,
            structureMax = 100f,
        };
        bf.units.Add(caster);
        var spawned = BoardSummonWingService.SpawnFromCasterUnit(bf, caster, new Random(1));
        Assert.That(spawned, Is.EqualTo(0));
    }
}
