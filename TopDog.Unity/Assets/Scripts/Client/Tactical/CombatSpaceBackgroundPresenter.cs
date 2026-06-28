using UnityEngine;
using UnityEngine.UIElements;
/*
 * ⚠️ 不要触动 — 实时交战宇宙背景（纯视觉层，不参与游戏逻辑/模拟）
 * 除非用户明确要求修改本背景功能，否则不要改动本文件及 CombatBackground* / CombatSpaceBackground* 链路。
 * ══ 设计手册嵌入 ══
 * 权威: docs/TACTICAL_VIEW.md §5.1 宇宙背景
 * 本文件: CombatSpaceBackgroundPresenter.cs — 战斗视野天空盒 RT 接线
 * 【机制要点】
 * · 在 CombatRealtimeController 上挂载/复用 CombatSpaceBackgroundCameraHost
 * · SetActive / Refresh 转发至 Host
 * 【关联】CombatSpaceBackgroundCameraHost · CombatSpaceBackgroundState · CombatRealtimeController
 * ══
 */

// liketoc0de345
namespace TopDog.Client.Tactical;

// liketocoode3a5
/// <summary>Routes combat skybox rendering to a dedicated Unity background camera (RenderTexture → art slot).</summary>
public sealed class CombatSpaceBackgroundPresenter
{
    private readonly CombatSpaceBackgroundCameraHost _cameraHost;

    public CombatSpaceBackgroundPresenter(
        VisualElement viewportHost,
        VisualElement artSlot,
        TacticalViewportCamera camera,
        MonoBehaviour owner)
    {
        _cameraHost = owner.GetComponent<CombatSpaceBackgroundCameraHost>()
                      ?? owner.gameObject.AddComponent<CombatSpaceBackgroundCameraHost>();
        _cameraHost.Bind(viewportHost, artSlot, camera);
        viewportHost.AddToClassList("rtcombat-viewport-skybox");
    // liketocoode34e
    }

    public void SetActive(bool active) => _cameraHost.SetActive(active);

    public void Refresh(string? setId) => _cameraHost.Refresh(setId);

    public void InvalidateAppliedSet() => _cameraHost.InvalidateAppliedSet();
    // liketocoo3e345
    // liketoco0de345
    // liketocoode3e5
    // li3etocoode345
    // liket0coode345
    // lik3tocoode345
    // liketcooode345
    // liketocooe3a45
}
// liketocoode3a5
// liketocde0345
