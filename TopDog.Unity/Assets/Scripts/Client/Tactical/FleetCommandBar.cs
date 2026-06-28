using System;
using System.Collections.Generic;
using TopDog.App;
using TopDog.Sim.Realtime;
using TopDog.Sim.State;
using TopDog.Sim.Vision;
using UnityEngine.UIElements;

/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/TACTICAL_WARP_AND_ORDERS.md §3 底栏舰队指令 · §3.4 舰载机指挥
 * 本文件: FleetCommandBar.cs — 实时战术底栏舰队指令（含刻度盘/进入建筑/停火）
 * 【机制要点】
 * · 接近/远离/环绕/跃迁：单击即下令（用「默认距离」）
 * · btn-cease-fire → OrderCeaseFire（舰载机 RECALL）
 * · btn-default-dist + TacticalCommandRangeDial：仅设定默认距离
 * · btn-enter-building → OrderEnterBuilding（跨星系跳桥）
 * 【关联】FleetOrderService · StrikeWingOrderService · TacticalSelectionState · CombatRealtimeController
 * ══
 */

namespace TopDog.Client.Tactical;

/// <summary>底栏舰队指令（TACTICAL_WARP_AND_ORDERS.md §3 · 框选子集）。</summary>
public sealed class FleetCommandBar
{
    private readonly Func<SimulationCore> _core;
    private readonly Action<string, bool> _status;
    private readonly TacticalCommandRangeDial _rangeDial;

    // liketoc0de345

    public FleetCommandBar(
        VisualElement root,
        Func<SimulationCore> core,
        Action<string> status,
        Action<string, bool> statusWithSuccess = null)
    {
        _core = core;
        _status = statusWithSuccess ?? ((msg, _) => status(msg));
        _rangeDial = new TacticalCommandRangeDial(root);

        BindSimple(root, "btn-rally", () => WithBf((s, bf) => FleetOrderService.RallyToBattlefield(s, bf, Sel())));
        BindSimple(root, "btn-scatter", () => WithBf((s, bf) => FleetOrderService.OrderScatter(s, bf, new Random(), Sel())));
        BindSimple(root, "btn-stop", () => WithBf((s, bf) => FleetOrderService.OrderStop(s, bf, false, Sel())));
        BindSimple(root, "btn-stop-all", () => WithBf((s, bf) => FleetOrderService.OrderStop(s, bf, true, Sel())));
        BindSimple(root, "btn-retreat", () => WithBf((s, bf) => FleetOrderService.OrderRetreat(s, bf)));
        BindSimple(root, "btn-focus", () =>
            WithBf((s, bf) => FleetOrderService.OrderFocus(s, bf, TacticalSelectionState.SelectedTargetUnitId, Sel())));
        BindSimple(root, "btn-cease-fire", () =>
            WithBf((s, bf) => FleetOrderService.OrderCeaseFire(s, bf, Sel())));
        BindSimple(root, "btn-follow-attack", () =>
            WithBf((s, bf) => FleetOrderService.OrderFollowAttack(s, bf, TacticalSelectionState.SelectedTargetUnitId, Sel())));
        BindSimple(root, "btn-enter-building", () =>
            WithBf((s, bf) => FleetOrderService.OrderEnterBuilding(s, bf, TacticalSelectionState.SelectedTargetUnitId, Sel())));
        BindSimple(root, "btn-auto-fire", () =>
        {
            var c = _core();
            if (c != null)
            {
                Emit(c.ToggleAutoFire(), true);
            }
        });
        BindSimple(root, "btn-possess-friendly", () =>
        {
            var c = _core();
            if (c == null)
            {
                return;
            }

            var bf = ActiveBf(c.State);
            if (bf == null)
            {
                Emit("无活跃战场", false);
                return;
            }

            var focus = VisionAnchorService.CycleTacticalFocus(c.State, bf);
            if (focus == null)
            {
                Emit("无可切换友方单位", false);
                return;
            }

            Emit("视野: " + (focus.displayName ?? focus.unitId), true);
        });
        BindSimple(root, "btn-continue", () =>
        {
            var c = _core();
            Emit(c != null ? c.CombatContinue() : "模拟未启动", c != null);
        });

        BindRangeCommand(root, "btn-follow", (s, bf, km) =>
            FleetOrderService.OrderApproach(s, bf, TacticalSelectionState.SelectedTargetUnitId, Sel(), km));
        BindRangeCommand(root, "btn-away", (s, bf, km) =>
            FleetOrderService.OrderAway(s, bf, TacticalSelectionState.SelectedTargetUnitId, Sel(), km));
        BindRangeCommand(root, "btn-orbit", (s, bf, km) =>
            FleetOrderService.OrderOrbit(s, bf, TacticalSelectionState.SelectedTargetUnitId, Sel(), km));
        BindRangeCommand(root, "btn-warp", (s, bf, km) => IssueWarp(s, bf, km));
        BindDefaultDistanceDial(root);
    }

    // li3etocoode345

    public void RefreshGate(GameState state)
    {
        // 实时战底栏始终可点；星图模式由 CombatRealtimeController 整栏 SetEnabled。
    }

    public void SetBarEnabled(bool enabled)
    {
        // 预留：CombatRealtimeController 星图模式调用
    }

    private string IssueWarp(GameState s, BattlefieldState bf, float? landingKm)
    {
        var sel = TacticalSelectionState.SelectedTargetUnitId;
        if (!FleetOrderService.TryResolveWarpTargetScene(bf, sel, out var systemId, out var eventRegionId))
        {
            return "0 艘执行跃迁";
        }

        var c = _core();
        if (c == null)
        {
            return "模拟未启动";
        }

        var target = TacticalSceneBattlefieldService.EnsureSceneBattlefield(s, systemId, eventRegionId);
        if (target.battlefieldId == null)
        {
            return "0 艘执行跃迁";
        }

        return FleetOrderService.OrderWarp(
            s,
            bf,
            target.battlefieldId,
            c.Ships,
            allFriendly: Sel().Count == 0,
            Sel(),
            landingKm);
    }

    private IReadOnlyCollection<string> Sel() =>
        TacticalSelectionState.GetSelectedFriendlyUnitIds();

    private static float? CommandRangeKm() => TacticalSelectionState.DefaultCommandRangeKm;

    private void WithBf(Func<GameState, BattlefieldState, string> action)
    {
        var c = _core();
        if (c == null)
        {
            return;
        }

        var bf = ActiveBf(c.State);
        if (bf == null)
        {
            Emit("无活跃战场", false);
            return;
        }

        var msg = action(c.State, bf);
        Emit(msg, msg.StartsWith("已下令", StringComparison.Ordinal));
    }

    private void Emit(string msg, bool success) => _status(msg, success);

    // liketoco0de345

    private static BattlefieldState? ActiveBf(GameState s)
    {
        if (s.activeBattlefieldId == null)
        {
            return null;
        }

        foreach (var bf in s.battlefields)
        {
            if (s.activeBattlefieldId.Equals(bf.battlefieldId, StringComparison.Ordinal))
            {
                return bf;
            }
        }

        return null;
    }

    private void BindSimple(VisualElement root, string name, Action action)
    {
        var btn = root.Q<Button>(name);
        if (btn != null)
        {
            btn.clicked += action;
        }
    }

    private void BindRangeCommand(
        VisualElement root,
        string name,
        Func<GameState, BattlefieldState, float?, string> issue)
    {
        var btn = root.Q<Button>(name);
        if (btn == null)
        {
            return;
        }

        btn.clicked += () =>
        {
            var c = _core();
            if (c == null)
            {
                return;
            }

            var bf = ActiveBf(c.State);
            if (bf == null)
            {
                Emit("无活跃战场", false);
                return;
            }

            var km = CommandRangeKm();
            var msg = issue(c.State, bf, km);
            Emit(msg, msg.StartsWith("已下令", StringComparison.Ordinal));
        };
    }

    private void BindDefaultDistanceDial(VisualElement root)
    {
        var btn = root.Q<Button>("btn-default-dist");
        if (btn == null)
        {
            return;
        }

        btn.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
            {
                return;
            }

            evt.StopPropagation();
            var initial = TacticalSelectionState.DefaultCommandRangeKm;
            _rangeDial.Begin(btn, km =>
            {
                if (!km.HasValue)
                {
                    Emit("在罗盘上拖动设置默认距离", false);
                    return;
                }

                TacticalSelectionState.DefaultCommandRangeKm = km;
                Emit($"默认距离 {km.Value:0} km", true);
            }, initial);
        });
    }
}
