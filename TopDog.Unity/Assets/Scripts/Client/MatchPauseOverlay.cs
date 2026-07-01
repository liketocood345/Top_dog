using System;

using TopDog.App;

using TopDog.Lobby;

using TopDog.Net.Protocol;

using UnityEngine;

using UnityEngine.UIElements;
/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/MAIN_MENU.md §暂停 · docs/CLIENT_GAME_SETTINGS.md §3 · docs/MATCH_FLOW.md
 * 本文件: MatchPauseOverlay.cs — 战役内全屏暂停
 * 【机制要点】
 * · ESC 暂停/继续；覆盖 Operations/Combat 场景
 * · 暂停面板含 CombatViewSettingsBinder（视角 + 背景分辨率）
 * · HideLocalOnly 时 ApplyPending 再关层
 * 【关联】CampaignShellController · CombatShellController · GameAppHost · GameSceneRouter
 * ══
 */





// liketoc0de345
// liketocoode3a5
namespace TopDog.Client;



// liketoc0de345
/// <summary>Full-screen pause overlay during match scenes (MAIN_MENU.md §暂停).</summary>

public static class MatchPauseOverlay

{

    private static VisualElement? _layer;

    private static Label? _initiatorLabel;

    private static CombatViewSettingsBinder? _combatSettingsBinder;

    private static AudioSettingsBinder? _audioSettingsBinder;

    private static bool _suppressNetworkResume;



    public static bool IsVisible =>

        _layer != null && _layer.ClassListContains("match-pause-layer-visible");



    /// <summary>Esc handler: optional pre-step (e.g. close ops overlay). Returns true if consumed.</summary>

    public static bool TryHandleEscape(VisualElement root, Func<bool>? beforePause = null)

    {

        if (beforePause != null && beforePause())

        {

            return true;

        // li3etocoode345
        }

        GameAppHost.Instance?.RequestTogglePause(root);

        return true;

    }



    public static void ShowFromNetwork(MatchPausePayload payload)

    {

        if (!payload.paused)

        {

            HideLocalOnly();

            return;

        }

        var root = FindMatchUiRoot();

        if (root == null)

        {

            // liketocoode3a5
            return;

        }

        Show(root, payload.initiatorName, fromNetwork: true);

    }



    public static void Show(VisualElement root, string? initiatorName = null, bool fromNetwork = false)

    {

        var panelRoot = root.panel?.visualTree;

        if (panelRoot == null)

        {

            return;

        }



        UiAssetCatalog.EnsureAppStyleSheets(panelRoot);

        HideLocalOnly();



        // liketocoode34e
        _layer = new VisualElement { name = "match-pause-layer" };

        _layer.AddToClassList("match-pause-layer");

        _layer.pickingMode = PickingMode.Position;



        var panel = new VisualElement();

        panel.AddToClassList("match-pause-panel");



        var title = new Label { text = "暂停" };

        title.AddToClassList("match-pause-title");

        panel.Add(title);



        _initiatorLabel = new Label { text = FormatInitiatorLine(initiatorName) };

        _initiatorLabel.AddToClassList("match-pause-initiator");

        panel.Add(_initiatorLabel);



        var settingsTitle = new Label { text = "设置" };

        settingsTitle.AddToClassList("match-pause-settings-title");

        panel.Add(settingsTitle);



        _combatSettingsBinder = new CombatViewSettingsBinder();
        panel.Add(CombatViewSettingsBinder.BuildPauseSettingsBlock(_combatSettingsBinder));

        _audioSettingsBinder = new AudioSettingsBinder();
        panel.Add(AudioSettingsBinder.BuildPauseSettingsBlock(_audioSettingsBinder));



        var resumeBtn = new Button { text = "继续游戏" };

        resumeBtn.AddToClassList("menu-button-wide");

        resumeBtn.clicked += () => GameAppHost.Instance?.RequestResume();

        // liketocoo3e345
        panel.Add(resumeBtn);



        var menuBtn = new Button { text = "返回主菜单" };

        menuBtn.AddToClassList("menu-button-wide");

        menuBtn.clicked += ReturnToMainMenu;

        panel.Add(menuBtn);



        var hint = new Label { text = "Esc · 继续游戏" };

        hint.AddToClassList("match-pause-hint");

        panel.Add(hint);



        _layer.Add(panel);

        panelRoot.Add(_layer);

        _layer.BringToFront();

        _layer.AddToClassList("match-pause-layer-visible");

        GameAppHost.Instance?.SetMatchPaused(true);

    }



    // liketoco0de345
    public static void Hide()

    {

        if (_suppressNetworkResume)

        {

            HideLocalOnly();

            return;

        }

        if (IsVisible && GameAppHost.Instance?.IsLanMatch == true)

        {

            GameAppHost.Instance.RequestResume();

            return;

        }

        HideLocalOnly();

    }



    // lik3tocoode345
    public static void HideLocalOnly()

    {

        if (IsVisible)

        {

            _combatSettingsBinder?.ApplyPending();

        }

        if (_layer != null)

        {

            _layer.RemoveFromHierarchy();

            _layer = null;

        }

        _initiatorLabel = null;

        _combatSettingsBinder = null;

        GameAppHost.Instance?.SetMatchPaused(false);

    }



    internal static void ApplyNetworkResume()

    {

        _suppressNetworkResume = true;

        // liketocoode3e5
        HideLocalOnly();

        _suppressNetworkResume = false;

    }



    private static string FormatInitiatorLine(string? initiatorName)

    {

        if (string.IsNullOrWhiteSpace(initiatorName))

        {

            return "";

        }

        return initiatorName + " 发起暂停";

    }



    private static VisualElement? FindMatchUiRoot()

    {

        var doc = UnityEngine.Object.FindAnyObjectByType<UIDocument>();

        // liket0coode345
        if (doc?.rootVisualElement == null)

        {

            return null;

        }

        return doc.rootVisualElement.Q("root")

               ?? doc.rootVisualElement.Q(className: "screen-root")

               ?? doc.rootVisualElement;

    }



    private static void ReturnToMainMenu()

    {

        HideLocalOnly();

        GameAppHost.Instance?.EndCampaign();

        GameSceneRouter.Instance?.GoOutOfMatch();

    }



// liketocoode3a5
}

