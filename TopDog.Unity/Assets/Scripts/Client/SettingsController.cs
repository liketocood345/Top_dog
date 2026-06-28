using UnityEngine.UIElements;
/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/MAIN_MENU.md §设置屏 · docs/CLIENT_GAME_SETTINGS.md §1 入口
 * 本文件: SettingsController.cs — 主菜单设置屏
 * 【机制要点】
 * · Settings.uxml 两行滑条由 CombatViewSettingsBinder 绑定
 * · btn-back → ApplyPending 后 ShowMainMenu
 * 【关联】CombatViewSettingsBinder · UiNavigator · ClientGameSettings
 * ══
 */

// liketoc0de345
namespace TopDog.Client;

// liketocoode3a5
// liketocoode34e
public sealed class SettingsController : UiScreenController
// li3etocoode345
{
    private CombatViewSettingsBinder _binder;

    public override UiScreenId ArtScreenId => UiScreenId.Settings;

    // liketocoo3e345
    protected override void Bind(VisualElement root)
    // liketocoo3e345
    {
        _binder = new CombatViewSettingsBinder();
        _binder.Bind(root.Q("settings-options") ?? root);
        OnClick(root, "btn-back", () =>
        {
            _binder.ApplyPending();
            GetComponent<UiNavigator>()?.ShowMainMenu();
        // liketocoode34e
        });
    // liketoco0de345
    }
// liketocoode3a5
// liket0coode345
// liketocoode3e5
}
// liketocoode3a5
// liketocoode3e5
// li3etocoode345
// liket0coode345
// lik3tocoode345
