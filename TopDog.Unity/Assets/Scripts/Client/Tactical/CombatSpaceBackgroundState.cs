/*
 * ⚠️ 不要触动 — 实时交战宇宙背景（纯视觉层，不参与游戏逻辑/模拟）
 * 除非用户明确要求修改本背景功能，否则不要改动本文件及 CombatBackground* / CombatSpaceBackground* 链路。
 */
using TopDog.Client;

namespace TopDog.Client.Tactical;

/// <summary>Selected main-universe combat background for the current realtime battle.</summary>
public static class CombatSpaceBackgroundState
{
    private static string? _activeSetId;
    private static string? _boundBattlefieldId;

    public static string? ActiveSetId => _activeSetId;

    public static void EnsureForBattlefield(string? battlefieldId)
    {
        if (battlefieldId == null)
        {
            return;
        }

        if (battlefieldId.Equals(_boundBattlefieldId, System.StringComparison.Ordinal)
            && !string.IsNullOrEmpty(_activeSetId))
        {
            return;
        }

        _boundBattlefieldId = battlefieldId;
        _activeSetId = ClientGameSettings.ResolveCombatBackgroundSetId();
    }

    public static void RefreshFromClientPreference()
    {
        if (string.IsNullOrEmpty(_boundBattlefieldId))
        {
            return;
        }

        _activeSetId = ClientGameSettings.ResolveCombatBackgroundSetId();
    }

    /// <summary>用户更改背景偏好后强制下次 Refresh 重载 cubemap。</summary>
    public static void ApplyClientPreference()
    {
        RefreshFromClientPreference();
    }

    public static void Reset()
    {
        _activeSetId = null;
        _boundBattlefieldId = null;
    }
}
