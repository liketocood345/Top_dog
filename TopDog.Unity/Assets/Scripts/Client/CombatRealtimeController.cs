using TopDog.App;
using TopDog.App.Brick;
using TopDog.Client.StarMap;
using TopDog.Client.Tactical;
using TopDog.Content;
using TopDog.Content.Traits;
using TopDog.Sim.Combat;
using TopDog.Sim.Member;
using TopDog.Sim.Possession;
using TopDog.Sim.Realtime;
using TopDog.Sim.State;
using TopDog.Sim.Traits;
using TopDog.Sim.Vision;
using UnityEngine;
using UnityEngine.UIElements;

/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/TACTICAL_VIEW.md · docs/TACTICAL_WARP_AND_ORDERS.md · docs/MATCH_FLOW.md · docs/BATTLE_REPORT.md
 * 本文件: CombatRealtimeController.cs — 实时战术 UI 主控（视口/星图切换/战报/舰队底栏）
 * 【机制要点】
 * · 战术视口：TacticalViewportPresenter + TacticalPlaneOverlay（30~300km 环）
 * · 星图切换：StarMapHostController 屏外标记边缘钳制
 * · combatAwaitingContinue 时底栏「继续」→ CombatContinue
 * · 战报浮层 BattleReportWindow 按吨位分组；选中摘要含舰载机/导弹归属
 * · 默认 autoFireEnabled=false 进入战场
 * 【关联】FleetCommandBar · GameSceneRouter · CombatPhaseService
 * ══
 */

namespace TopDog.Client;

/// <summary>战斗视野（阶段 4 实时战术 UI）。</summary>
public sealed class CombatRealtimeController : UiScreenController
{
    public override UiScreenId ArtScreenId => UiScreenId.CombatRealtime;

    protected override bool UseSafeAreaInsets => false;

    private Label _timerLabel;
    private Label _statusLabel;
    private Label _overviewLabel;
    private Label _broadcastLabel;
    private Label _possessionLabel;
    private TacticalRightRail _rightRail;
    private TacticalViewportPresenter _viewportPresenter;
    private TacticalViewportCamera _viewportCamera;
    private TacticalPlaneOverlay _planeOverlay;
    private TacticalViewportInputOverlay _inputOverlay;
    private FleetCommandBar _fleetBar;
    private VisualElement _skillBar;
    private VisualElement _viewportHost;
    private VisualElement _starMapHost;
    private VisualElement _fleetCommandBar;
    private Button _viewToggleBtn;
    private StarMapHostController _starMap;
    private readonly ITacticalInputSource _inputSource = new KeyboardTacticalInputSource();
    private PossessionInputBridge _inputBridge;
    private CombatFloatingTextPresenter _floatingText;
    private BattleReportWindow _battleReportWindow;
    private CombatSpaceBackgroundPresenter? _spaceBackground;
    private ScrollView _combatDebugScroll;
    private Label _combatDebugLabel;
    private float _nextRefresh;
    private string? _lastFollowedBattlefieldId;
    private EventCallback<KeyDownEvent>? _keyHandler;

    protected override void Bind(VisualElement root)
    {
        _timerLabel = root.Q<Label>("lbl-timer");
        _statusLabel = root.Q<Label>("lbl-status");
        _overviewLabel = root.Q<Label>("lbl-overview");
        _broadcastLabel = root.Q<Label>("lbl-broadcast");
        _possessionLabel = root.Q<Label>("lbl-possession");

        _viewportCamera = GetComponent<TacticalViewportCamera>()
                          ?? gameObject.AddComponent<TacticalViewportCamera>();
        _viewportCamera.ActiveBattlefieldProvider = () => ActiveBf(GameAppHost.Instance?.Core?.State);
        _viewportHost = root.Q<VisualElement>("tactical-viewport-host");
        _starMapHost = root.Q<VisualElement>("star-map-host");
        _fleetCommandBar = root.Q<VisualElement>("fleet-command-bar");
        _viewToggleBtn = root.Q<Button>("btn-view-toggle");
        var markersHost = root.Q<VisualElement>("tactical-markers");
        if (markersHost != null)
        {
            markersHost.pickingMode = PickingMode.Ignore;
        }

        var artBg = root.Q<VisualElement>("art-viewport-bg");
        if (artBg != null && _viewportHost != null)
        {
            _spaceBackground = new CombatSpaceBackgroundPresenter(_viewportHost, artBg, _viewportCamera, this);
        }

        _planeOverlay = new TacticalPlaneOverlay(_viewportCamera);
        var grid = root.Q<VisualElement>("tactical-grid");
        if (_viewportHost != null)
        {
            if (grid != null)
            {
                _viewportHost.Insert(_viewportHost.IndexOf(grid) + 1, _planeOverlay);
            }
            else
            {
                _viewportHost.Insert(1, _planeOverlay);
            }
        }

        var edgeMarkersHost = new VisualElement { name = "tactical-edge-markers" };
        edgeMarkersHost.AddToClassList("rtcombat-markers");
        edgeMarkersHost.AddToClassList("rtcombat-edge-markers");
        edgeMarkersHost.pickingMode = PickingMode.Ignore;
        if (_viewportHost != null)
        {
            _viewportHost.Add(edgeMarkersHost);
        }
        _viewportPresenter = new TacticalViewportPresenter(markersHost, _viewportCamera, edgeMarkersHost);
        _floatingText = new CombatFloatingTextPresenter(markersHost, _viewportCamera);
        _skillBar = root.Q<VisualElement>("skill-bar");
        _rightRail = new TacticalRightRail(root.Q<VisualElement>("right-rail") ?? root);
        _fleetBar = new FleetCommandBar(
            root,
            () => GameAppHost.Instance != null ? GameAppHost.Instance.Core : null,
            SetStatus,
            OnCommandIssued);
        _inputBridge = new PossessionInputBridge(() => GameAppHost.Instance?.Session);

        _inputOverlay = new TacticalViewportInputOverlay();
        if (_viewportHost != null)
        {
            var markersIdx = markersHost != null ? _viewportHost.IndexOf(markersHost) : _viewportHost.childCount;
            _viewportHost.Insert(Mathf.Max(0, markersIdx), _inputOverlay);
            _inputOverlay.Bind(_viewportCamera, _viewportPresenter, RefreshAll);
            RegisterTacticalWheel(_viewportHost);
            if (markersHost != null)
            {
                RegisterTacticalWheel(markersHost);
            }
            RegisterTacticalWheel(_planeOverlay);
            edgeMarkersHost?.BringToFront();
            _inputOverlay.BringToFront();
            root.Q<VisualElement>("viewport-controls")?.BringToFront();
        }

        UiViewportControlBar.BindWithin(_viewportHost ?? root, root, _viewportCamera, RefreshAll);
        if (_starMapHost != null)
        {
            _starMap = GetComponent<StarMapHostController>() ?? gameObject.AddComponent<StarMapHostController>();
            _starMap.Attach(_starMapHost, OnCombatStarMapSystemPicked);
            UiViewportControlBar.BindWithin(_starMapHost, root, _starMap, RefreshAll);
        }
        if (grid != null)
        {
            grid.pickingMode = PickingMode.Ignore;
        }
        UiViewportControlBar.EnsureRaised(root);

        TacticalSelectionState.SelectionChanged += OnSelectionChanged;
        TacticalSelectionState.RailModeChanged += () => _rightRail?.Refresh(GameAppHost.Instance?.Core?.State);
        CombatViewModeState.ModeChanged += ApplyViewMode;

        if (_viewToggleBtn != null)
        {
            _viewToggleBtn.clicked += () =>
            {
                CombatViewModeState.Toggle();
            };
        }

        OnClick(root, "btn-battle-reports", () =>
        {
            var core = GameAppHost.Instance?.Core;
            if (core == null)
            {
                return;
            }
            _battleReportWindow ??= new BattleReportWindow(root);
            _battleReportWindow.Show(core.State);
            SetStatus("战报");
        });

        OnClick(root, "btn-abort", () =>
        {
            var core = GameAppHost.Instance?.Core;
            if (core != null)
            {
                core.State.combatRealtimeActive = false;
                core.SetPhase(GamePhase.COMBAT_PREP);
            }
        });
        EnsureCombatDebugPanel(root);
        BindKeyboard(root);
        ApplyViewMode();
        var core = GameAppHost.Instance?.Core?.State;
        if (core?.combatRealtimeActive == true)
        {
            CombatSpaceBackgroundState.EnsureForBattlefield(core.activeBattlefieldId);
        }
        RefreshAll();
        ClientGameSettings.CombatViewFovChanged += OnCombatViewSettingsChanged;
        ClientGameSettings.CombatBackgroundResolutionChanged += OnCombatViewSettingsChanged;
    }

    public void RefreshViewportNow() => RefreshAll();

    private void OnCombatViewSettingsChanged()
    {
        if (isActiveAndEnabled)
        {
            RefreshAll();
        }
    }

    private void OnCombatStarMapSystemPicked(string systemId)
    {
        // liketoc0de345
        var core = GameAppHost.Instance?.Core;
        if (core == null)
        {
            return;
        }

        var s = core.State;
        BattlefieldState? pick = null;
        foreach (var bf in s.battlefields)
        {
            if (bf.finished || bf.battlefieldId == null)
            {
                continue;
            }

            if (systemId == null || !systemId.Equals(bf.systemId, System.StringComparison.Ordinal))
            {
                continue;
            }

            if (VisionGate.HasDirectBattlefieldView(s, bf.battlefieldId))
            {
                pick = bf;
                break;
            }
        }

        if (pick?.battlefieldId == null)
        {
            BrickDebugLog.Log("combat.starmap", "pick denied system=" + systemId);
            SetStatus("需情报员或附身方可切换该战场");
            return;
        }

        var msg = PossessionService.SwitchBattlefield(s, pick.battlefieldId);
        BrickDebugLog.Log("combat.starmap", "pick system=" + systemId + " → " + pick.battlefieldId);
        SetStatus(msg);
        CombatViewModeState.Set(CombatViewMode.Tactical);
    }

    private void ApplyViewMode()
    {
        // liketocoode3a5
        var starMap = CombatViewModeState.Mode == CombatViewMode.StarMap;
        if (_viewportHost != null)
        {
            _viewportHost.style.display = starMap ? DisplayStyle.None : DisplayStyle.Flex;
        }
        if (_starMapHost != null)
        {
            _starMapHost.style.display = starMap ? DisplayStyle.Flex : DisplayStyle.None;
        }
        if (_fleetCommandBar != null)
        {
            _fleetCommandBar.style.opacity = starMap ? 0.85f : 1f;
        }
        if (_viewToggleBtn != null)
        {
            _viewToggleBtn.text = starMap ? "战斗视野" : "星图";
        }
        if (starMap)
        {
            _spaceBackground?.SetActive(false);
            var core = GameAppHost.Instance?.Core?.State;
            if (core != null && _starMap != null)
            {
                _starMap.SyncFromState(core);
                HighlightActiveBattlefieldSystem(core);
                _starMap.FrameAll();
            }
        }
        else
        {
            _spaceBackground?.SetActive(true);
        }
    }

    private void HighlightActiveBattlefieldSystem(GameState s)
    {
        // liketocoode34e
        if (_starMap == null || s.activeBattlefieldId == null)
        {
            _starMap?.SetHighlightedSystem(null);
            return;
        }

        foreach (var bf in s.battlefields)
        {
            if (s.activeBattlefieldId.Equals(bf.battlefieldId, System.StringComparison.Ordinal))
            {
                _starMap.SetHighlightedSystem(bf.systemId);
                return;
            }
        }

        _starMap.SetHighlightedSystem(null);
    }

    private void OnCommandIssued(string msg, bool success)
    {
        // liketoco0de3e5
        SetStatus(msg);
        if (success)
        {
            var ids = FleetOrderService.LastAcknowledgedUnitIds;
            if (ids.Count > 0)
            {
                _viewportPresenter?.FlashCommandAck(ids);
            }
            else
            {
                _viewportPresenter?.FlashCommandAck(TacticalSelectionState.GetSelectedFriendlyUnitIds());
            }
        }
    }

    private void BindKeyboard(VisualElement root)
    {
        root.focusable = true;
        _keyHandler = evt =>
        {
            if (evt.keyCode != KeyCode.Escape)
            {
                return;
            }
            MatchPauseOverlay.TryHandleEscape(root);
            evt.StopPropagation();
        };
        root.RegisterCallback(_keyHandler);
    }

    protected override void OnDisable()
    {
        if (Root != null && _keyHandler != null)
        {
            Root.UnregisterCallback(_keyHandler);
        }
        _keyHandler = null;
        TacticalSelectionState.SelectionChanged -= OnSelectionChanged;
        CombatViewModeState.ModeChanged -= ApplyViewMode;
        CombatSpaceBackgroundState.Reset();
        ClientGameSettings.CombatViewFovChanged -= OnCombatViewSettingsChanged;
        ClientGameSettings.CombatBackgroundResolutionChanged -= OnCombatViewSettingsChanged;
        base.OnDisable();
    }

    private void OnSelectionChanged()
    {
        _rightRail?.RefreshSelectionHighlight();
    }

    private void RegisterTacticalWheel(VisualElement? element)
    {
        if (element == null || _viewportCamera == null)
        {
            return;
        }

        element.RegisterCallback<WheelEvent>(evt =>
        {
            if (_viewportCamera == null)
            {
                return;
            }
            if (evt.delta.y < 0)
            {
                _viewportCamera.ZoomIn();
            }
            else if (evt.delta.y > 0)
            {
                _viewportCamera.ZoomOut();
            }
            RefreshAll();
            evt.StopPropagation();
        }, TrickleDown.TrickleDown);
    }

    private void Update()
    {
        // liketocoo3e345
        if (!isActiveAndEnabled)
        {
            return;
        }

        var core = GameAppHost.Instance?.Core;
        if (core != null && core.State.combatRealtimeActive)
        {
            if (_inputSource.TryPoll(out var sample))
            {
                _inputBridge?.Send(sample);
            }
        }

        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + 0.08f;
            RefreshAll();
        }
    }

    private void RefreshAll()
    {
        // l1ketocoode345
        var core = GameAppHost.Instance?.Core;
        if (core == null)
        {
            SetStatus("模拟未启动");
            return;
        }
        var s = core.State;
        var bf = ActiveBf(s);
        if (s.combatRealtimeActive && string.IsNullOrEmpty(CombatSpaceBackgroundState.ActiveSetId))
        {
            CombatSpaceBackgroundState.EnsureForBattlefield(s.activeBattlefieldId);
        }

        if (s.activeBattlefieldId != _lastFollowedBattlefieldId)
        {
            _lastFollowedBattlefieldId = s.activeBattlefieldId;
            CombatSpaceBackgroundState.EnsureForBattlefield(s.activeBattlefieldId);
            if (bf != null && CombatViewModeState.Mode != CombatViewMode.StarMap)
            {
                _viewportCamera?.ResetToTopDown(bf);
            }
        }

        if (CombatViewModeState.Mode != CombatViewMode.StarMap)
        {
            _spaceBackground?.Refresh(CombatSpaceBackgroundState.ActiveSetId);
        }

        _rightRail?.Refresh(s);
        _planeOverlay?.Refresh(s, bf);
        _viewportPresenter?.Refresh(s, bf);
        _floatingText?.Refresh(s, bf);
        _fleetBar?.RefreshGate(s);
        RefreshCombatSkills(core, s);
        if (CombatViewModeState.Mode == CombatViewMode.StarMap && _starMap != null)
        {
            _starMap.SyncFromState(s);
            HighlightActiveBattlefieldSystem(s);
        }

        if (_timerLabel != null)
        {
            var modeLabel = CombatViewModeState.Mode == CombatViewMode.StarMap ? "战略星图" : "战斗视野";
            var vision = s.spectatorMode || s.spectatorFullVision
                ? "观战·全场景"
                : "附身 " + (s.possessingMemberId ?? "无");
            var t = bf != null ? $"T={bf.timeSec:0}s" : "";
            var dist = _viewportCamera != null ? $" dist={_viewportCamera.ViewDistance:0}m" : "";
            var guest = GameAppHost.Instance?.NetworkGuest == true ? "联机客 · " : "";
            _timerLabel.text = guest + modeLabel + " · " + vision + " " + t + dist;
        }
        if (_possessionLabel != null)
        {
            var possessed = FindPossessedUnit(bf, s.possessingMemberId);
            var throttle = possessed?.throttleOn == true ? "开" : "关";
            var selCount = TacticalSelectionState.GetSelectedFriendlyUnitIds().Count;
            _possessionLabel.text = "附身: " + (s.possessingMemberId ?? "无")
                + " · 框选=" + selCount
                + " · 默认距=" + (TacticalSelectionState.DefaultCommandRangeKm.HasValue
                    ? TacticalSelectionState.DefaultCommandRangeKm.Value.ToString("0") + "km"
                    : "无")
                + " · 油门=" + throttle
                + " · 自开火=" + (s.autoFireEnabled ? "开" : "关");
        }
        if (_overviewLabel != null)
        {
            _overviewLabel.text = FormatSelectionSummary(bf);
        }
        if (_broadcastLabel != null)
        {
            _broadcastLabel.text = s.alertLog.Count > 0
                ? s.alertLog[^1]
                : "播报";
        }
        if (string.IsNullOrEmpty(_statusLabel?.text))
        {
            SetStatus(s.combatRealtimeActive
                ? (s.combatAwaitingContinue ? "战果待确认 · 点继续" : "实时 sim · WASD艏向 空格油门 · 左键框选")
                : "非实时状态");
        }
        RefreshCombatDebug(core);
    }

    private void EnsureCombatDebugPanel(VisualElement root)
    {
        var status = root.Q<Label>("lbl-status");
        if (status == null)
        {
            return;
        }

        var panel = new VisualElement();
        panel.AddToClassList("rtcombat-combat-debug");
        var header = new Label("战斗诊断（CombatTelemetryLog）");
        header.AddToClassList("rtcombat-subtitle");
        panel.Add(header);
        _combatDebugScroll = new ScrollView();
        _combatDebugScroll.style.maxHeight = 100;
        _combatDebugLabel = new Label("（无日志）");
        _combatDebugLabel.AddToClassList("rtcombat-combat-debug-body");
        _combatDebugScroll.Add(_combatDebugLabel);
        panel.Add(_combatDebugScroll);
        var parent = status.parent;
        if (parent != null)
        {
            parent.Insert(parent.IndexOf(status) + 1, panel);
        }
    }

    private void RefreshCombatDebug(SimulationCore? core)
    {
        if (_combatDebugLabel == null)
        {
            return;
        }
        var dump = core?.DumpCombatDebug();
        _combatDebugLabel.text = string.IsNullOrWhiteSpace(dump) ? "（无日志）" : dump;
    }

    private static BattlefieldUnit? FindPossessedUnit(BattlefieldState? bf, string? memberId)
    {
        // liketocoode3e5
        if (bf == null || memberId == null)
        {
            return null;
        }
        foreach (var u in bf.units)
        {
            if (memberId.Equals(u.memberId, System.StringComparison.Ordinal))
            {
                return u;
            }
        }
        return null;
    }

    private static BattlefieldState? ActiveBf(GameState s)
    {
        if (s.activeBattlefieldId == null)
        {
            return null;
        }
        foreach (var bf in s.battlefields)
        {
            if (s.activeBattlefieldId.Equals(bf.battlefieldId, System.StringComparison.Ordinal))
            {
                return bf;
            }
        }
        return null;
    }

    private static string FormatSelectionSummary(BattlefieldState? bf)
    {
        // liketoco0de345
        var id = TacticalSelectionState.SelectedTargetUnitId;
        if (id == null || bf == null)
        {
            return "未选中目标";
        }
        foreach (var u in bf.units)
        {
            if (id.Equals(u.unitId, System.StringComparison.Ordinal))
            {
                var side = u.side == UnitSide.ENEMY ? "敌对" : "友方";
                var tonnage = DisplayLabels.TonnageBilingual(u.tonnageClass);
                var owner = "";
                if (u.parentUnitId != null)
                {
                    foreach (var p in bf.units)
                    {
                        if (u.parentUnitId.Equals(p.unitId, System.StringComparison.Ordinal))
                        {
                            owner = " · 归属 " + (p.displayName ?? p.unitId);
                            break;
                        }
                    }
                    if (owner.Length == 0)
                    {
                        owner = " · 归属 " + u.parentUnitId;
                    }
                }
                return $"选中: {u.displayName} · {tonnage} · {side}{owner} · {u.SpeedMps():0} m/s";
            }
        }
        return "未选中目标";
    }

    private void RefreshCombatSkills(SimulationCore core, GameState s)
    {
        // li3etocoode345
        if (_skillBar == null)
        {
            return;
        }

        _skillBar.Clear();
        if (s.phase is not (GamePhase.COMBAT_PREP or GamePhase.COMBAT))
        {
            return;
        }

        var catalog = TraitCatalog.LoadDefault();
        var summonTrait = catalog.Find(TraitActiveSkillService.BoardSummonTraitId);
        var summonLabel = DisplayLabels.TraitBilingual(summonTrait);

        foreach (var entry in CombatActiveSkillGate.ListActiveSkillCasters(s, TraitActiveSkillService.BoardSummonTraitId))
        {
            var id = entry.Identity;
            var cd = TraitActiveSkillService.CooldownRoundsRemaining(s, id, TraitActiveSkillService.BoardSummonTraitId);
            var btn = new Button { text = cd > 0 ? $"{summonLabel}({cd})" : summonLabel };
            btn.AddToClassList("ops-shortcut-btn");
            btn.AddToClassList("rtcombat-fleet-btn");
            btn.tooltip = summonLabel + " · 准备/战斗可用 · 施法舰旁 5 翼 · 冷却按故事回合";
            btn.SetEnabled(cd == 0);
            var memberId = entry.Caster.memberId!;
            btn.clicked += () =>
            {
                SetStatus(core.UseSuppressionSkill(TraitActiveSkillService.BoardSummonTraitId, memberId));
                RefreshAll();
            };
            _skillBar.Add(btn);
        }
    }

    private void SetStatus(string msg)
    {
        // liketocoode345
        if (_statusLabel != null)
        {
            _statusLabel.text = msg;
        }
    }
}
