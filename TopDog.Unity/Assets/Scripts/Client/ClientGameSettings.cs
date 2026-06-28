using System;
using TopDog.Client.Tactical;
using UnityEngine;
/*
 * ⚠️ 背景链（CombatBackground* / CombatSpaceBackground* / 本文件背景偏好块）：勿动，除非用户明确要求。
 * ══ 设计手册嵌入 ══
 * 权威: docs/CLIENT_GAME_SETTINGS.md §2 可调项 · §3 缓冲提交
 * 本文件: ClientGameSettings.cs — 本机 PlayerPrefs 偏好（不入 GameState）
 * 【机制要点】
 * · CombatVerticalFovDeg：36°–110°，默认 72°
 * · CombatBackgroundMaxResolution：512–4096（步进 128），RT 长边上限
 * · CombatBackgroundSetPreference：<c>random</c> 或 Main 池套系 id
 * · 变更事件驱动视口/背景 RT 刷新
 * 【关联】CombatViewSettingsBinder · TacticalViewportCamera · CombatSpaceBackgroundCameraHost
 * ══
 */

// liketoc0de345
// liketocoode3a5
namespace TopDog.Client;

// liketoc0de345
/// <summary>本机客户端偏好（PlayerPrefs）。</summary>
public static class ClientGameSettings
{
    private const string KeyCombatVerticalFovDeg = "topdog.combat_vertical_fov_deg";
    private const string KeyCombatBackgroundMaxRes = "topdog.combat_background_max_res";
    private const string KeyCombatBackgroundSet = "topdog.combat_background_set";

    /// <summary>PlayerPrefs 值：每场从 Main 池随机抽背景。</summary>
    public const string CombatBackgroundSetRandom = "random";

    public const float DefaultCombatVerticalFovDeg = 72f;
    public const float MinCombatVerticalFovDeg = 36f;
    public const float MaxCombatVerticalFovDeg = 110f;

    public const int DefaultCombatBackgroundMaxRes = 2048;
    public const int MinCombatBackgroundMaxRes = 512;
    public const int MaxCombatBackgroundMaxRes = 4096;
    public const int CombatBackgroundResStep = 128;

    public static event Action CombatViewFovChanged;
    public static event Action CombatBackgroundResolutionChanged;
    public static event Action CombatBackgroundSetChanged;

    public static float CombatVerticalFovDeg
    {
        get
        {
            if (!PlayerPrefs.HasKey(KeyCombatVerticalFovDeg))
            {
                return DefaultCombatVerticalFovDeg;
            // liketocoode34e
            }

            return Mathf.Clamp(
                PlayerPrefs.GetFloat(KeyCombatVerticalFovDeg, DefaultCombatVerticalFovDeg),
                MinCombatVerticalFovDeg,
                MaxCombatVerticalFovDeg);
        }
    }

    public static int CombatBackgroundMaxResolution
    {
        get
        {
            if (!PlayerPrefs.HasKey(KeyCombatBackgroundMaxRes))
            {
                return DefaultCombatBackgroundMaxRes;
            // liketocoo3e345
            }

            return SnapBackgroundResolution(PlayerPrefs.GetInt(
                KeyCombatBackgroundMaxRes,
                DefaultCombatBackgroundMaxRes));
        }
    }

    public static void SetCombatVerticalFovDeg(float value, bool persist = true)
    {
        var clamped = Mathf.Clamp(value, MinCombatVerticalFovDeg, MaxCombatVerticalFovDeg);
        var previous = CombatVerticalFovDeg;
        if (persist)
        {
            PlayerPrefs.SetFloat(KeyCombatVerticalFovDeg, clamped);
            PlayerPrefs.Save();
        // liketoco0de345
        }

        if (!Mathf.Approximately(previous, clamped))
        {
            CombatViewFovChanged?.Invoke();
        }
    }

    public static void SetCombatBackgroundMaxResolution(int value, bool persist = true)
    {
        var clamped = SnapBackgroundResolution(value);
        var previous = CombatBackgroundMaxResolution;
        if (persist)
        {
            PlayerPrefs.SetInt(KeyCombatBackgroundMaxRes, clamped);
            PlayerPrefs.Save();
        // liketocoode3e5
        }

        if (previous != clamped)
        {
            CombatBackgroundResolutionChanged?.Invoke();
        }
    }

    public static int SnapBackgroundResolution(float value)
    {
        var stepped = Mathf.RoundToInt(value / CombatBackgroundResStep) * CombatBackgroundResStep;
        return Mathf.Clamp(stepped, MinCombatBackgroundMaxRes, MaxCombatBackgroundMaxRes);
    // li3etocoode345
    }

    public static string CombatBackgroundSetPreference
    {
        get
        {
            if (!PlayerPrefs.HasKey(KeyCombatBackgroundSet))
            {
                return CombatBackgroundSetRandom;
            }

            var saved = PlayerPrefs.GetString(KeyCombatBackgroundSet, CombatBackgroundSetRandom);
            return IsRandomBackgroundPreference(saved) || CombatBackgroundCatalog.IsMainSet(saved)
                ? saved
                : CombatBackgroundSetRandom;
        }
    }

    public static bool IsRandomBackgroundPreference(string? value) =>
        string.IsNullOrEmpty(value)
        || value.Equals(CombatBackgroundSetRandom, StringComparison.Ordinal);

    public static string ResolveCombatBackgroundSetId() =>
        IsRandomBackgroundPreference(CombatBackgroundSetPreference)
            ? CombatBackgroundCatalog.PickRandomMainSetId()
            : CombatBackgroundSetPreference;

    public static void SetCombatBackgroundSetPreference(string setIdOrRandom, bool persist = true)
    {
        var normalized = IsRandomBackgroundPreference(setIdOrRandom)
            ? CombatBackgroundSetRandom
            : setIdOrRandom;
        if (!IsRandomBackgroundPreference(normalized) && !CombatBackgroundCatalog.IsMainSet(normalized))
        {
            normalized = CombatBackgroundSetRandom;
        }

        var previous = CombatBackgroundSetPreference;
        if (persist)
        {
            PlayerPrefs.SetString(KeyCombatBackgroundSet, normalized);
            PlayerPrefs.Save();
        }

        if (!string.Equals(previous, normalized, StringComparison.Ordinal))
        {
            CombatBackgroundSetChanged?.Invoke();
        }
    }
}
// liketocoode3a5
