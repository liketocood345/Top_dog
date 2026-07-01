using UnityEngine.UIElements;
/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/MAIN_MENU.md §设置屏 · docs/CLIENT_GAME_SETTINGS.md §1 入口
 * 本文件: SettingsController.cs — 主菜单设置屏
 * 【机制要点】
 * · Settings.uxml 战斗滑条由 CombatViewSettingsBinder；音频由 AudioSettingsBinder
 * · btn-back → ApplyPending 后 ShowMainMenu
 * 【关联】CombatViewSettingsBinder · AudioSettingsBinder · UiNavigator · ClientGameSettings
 * ══
 */

namespace TopDog.Client;

public sealed class SettingsController : UiScreenController
{
    private CombatViewSettingsBinder _combatBinder;
    private AudioSettingsBinder _audioBinder;

    public override UiScreenId ArtScreenId => UiScreenId.Settings;

    protected override void Bind(VisualElement root)
    {
        var options = root.Q("settings-options") ?? root;
        _combatBinder = new CombatViewSettingsBinder();
        _combatBinder.Bind(options);
        _audioBinder = new AudioSettingsBinder();
        _audioBinder.Bind(options);
        OnClick(root, "btn-back", () =>
        {
            _combatBinder.ApplyPending();
            GetComponent<UiNavigator>()?.ShowMainMenu();
        });
    }
}
