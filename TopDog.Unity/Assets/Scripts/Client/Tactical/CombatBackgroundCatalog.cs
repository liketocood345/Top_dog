/*
 * ⚠️ 不要触动 — 实时交战宇宙背景（纯视觉层，不参与游戏逻辑/模拟）
 * 除非用户明确要求修改本背景功能，否则不要改动本文件及 CombatBackground* / CombatSpaceBackground* 链路。
 * 当前面序/贴图映射已验收（U/O/R/S +Y↔-Y；N +X↔-X +Y↔-Y），勿为无关需求重写。
 */
using System;

using System.Collections.Generic;

using System.IO;

using UnityEngine;



namespace TopDog.Client.Tactical;



/// <summary>

/// Second Galaxy main-universe skybox textures for realtime combat viewport.

/// Reserve sets live under Art/CombatBackgrounds/Reserve (not used for random pick).

/// </summary>

public static class CombatBackgroundCatalog

{

    private const string EquirectFile = "equirect.png";

    private const string LegacyPanoramaFile = "panorama.png";



    /// <summary>
    /// SG / Unity cubemap face assignment (NOT the order of filenames on disk).
    /// Config: SkyBoxCubmapResID → bundled cubemap; runtime material uses <c>_SkyboxCubeMap</c>.
    /// Extracted PNGs use either <c>±X/±Y/±Z</c> suffix (SpaceBoxPRO, U_Skybox) or directional names (ProjectX).
    /// Unity <see cref="CubemapFace"/> enum: +X, +Y, +Z, -X, -Y, -Z → PositiveX..NegativeZ.
    /// </summary>
    private static readonly string[] FaceSuffixes = { "+X", "+Y", "+Z", "-X", "-Y", "-Z" };

    /// <summary>Fallback keywords when a face PNG has no ±-axis suffix (e.g. ProjectXSkyBox_Right).</summary>
    private static readonly string[][] FaceKeywordFallbacks =
    {
        new[] { "RIGHT" },
        new[] { "UP" },
        new[] { "FRONT" },
        new[] { "LEFT" },
        new[] { "DOWN" },
        new[] { "BACK" },
    };

    private static readonly CubemapFace[] FaceOrder =

    {

        CubemapFace.PositiveX,

        CubemapFace.PositiveY,

        CubemapFace.PositiveZ,

        CubemapFace.NegativeX,

        CubemapFace.NegativeY,

        CubemapFace.NegativeZ,

    };

    /// <summary>Unity cubemap slot → source PNG index (+X,+Y,+Z,-X,-Y,-Z). Identity = 0..5.</summary>
    private static readonly int[] IdentityFaceSources = { 0, 1, 2, 3, 4, 5 };

    /// <summary>U/O/R/S：SG 导出 +Y/-Y 与 Unity 采样上下对调（边缝分析 + 目视）。</summary>
    private static readonly int[] SwapUyFaceSources = { 0, 4, 2, 3, 1, 5 };

    /// <summary>SpaceBoxPRO：+X/-X 与 +Y/-Y 源文件均需对调（边缝 +X 最优，上下需 +Y 对调）。</summary>
    private static readonly int[] SwapUxUyFaceSources = { 3, 4, 2, 0, 1, 5 };

    private const int CubemapLayoutVersion = 2;

    private static readonly Dictionary<string, int[]> SetFaceSourceRemap = new(StringComparer.Ordinal)

    {

        ["U_Skybox_01"] = SwapUyFaceSources,

        ["O_Skybox_01"] = SwapUyFaceSources,

        ["R_Skybox_01"] = SwapUyFaceSources,

        ["S_Skybox_01"] = SwapUyFaceSources,

        ["N_Skybox_Arothe01"] = SwapUxUyFaceSources,

        ["Wormhole_Perel"] = SwapUxUyFaceSources,

    };



    public static readonly string[] MainSetIds =

    {

        "U_Skybox_01",

        "O_Skybox_01",

        "R_Skybox_01",

        "S_Skybox_01",

        "N_Skybox_Arothe01",

    };



    public static readonly string[] ReserveSetIds =

    {

        "Wormhole_Perel",

        "ProjectXSkyBox",

        "Nebula_NuminousGlow",

    };



    private static readonly Dictionary<string, Texture2D> TextureCache = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, Cubemap> CubemapCache = new(StringComparer.Ordinal);

    private static readonly System.Random Rng = new();



    public static string PickRandomMainSetId()

    {

        return MainSetIds[Rng.Next(MainSetIds.Length)];

    }



    public static string GetSetDisplayLabel(string setId) => setId switch

    {

        "U_Skybox_01" => "U 宇宙",

        "O_Skybox_01" => "O 宇宙",

        "R_Skybox_01" => "R 宇宙",

        "S_Skybox_01" => "S 宇宙",

        "N_Skybox_Arothe01" => "N 阿洛斯",

        _ => setId,

    };



    public static Cubemap? LoadCubemap(string setId, bool mainPoolOnly = true)

    {

        if (string.IsNullOrEmpty(setId))

        {

            return null;

        }



        var cacheKey = setId + ":L" + CubemapLayoutVersion;

        if (CubemapCache.TryGetValue(cacheKey, out var cached))

        {

            return cached;

        }



        var setDir = ResolveSetDirectory(setId, mainPoolOnly);

        if (setDir == null)

        {

            return null;

        }



        var facePaths = ResolveFacePaths(setDir);

        if (facePaths == null)

        {

            Debug.LogWarning("TopDog: combat cubemap faces missing in " + setDir);

            return null;

        }



        var faceSize = ReadFaceSize(facePaths[0]);

        if (faceSize <= 0)

        {

            return null;

        }



        var cubemap = new Cubemap(faceSize, TextureFormat.RGBA32, false);

        var sourceRemap = SetFaceSourceRemap.TryGetValue(setId, out var remap) ? remap : IdentityFaceSources;

        for (var slot = 0; slot < FaceOrder.Length; slot++)

        {

            var sourceIndex = sourceRemap[slot];

            if (sourceIndex < 0 || sourceIndex >= facePaths.Length)

            {

                UnityEngine.Object.Destroy(cubemap);

                return null;

            }



            var faceTex = LoadFaceTexture(facePaths[sourceIndex]);

            if (faceTex == null)

            {

                UnityEngine.Object.Destroy(cubemap);

                return null;

            }



            cubemap.SetPixels(faceTex.GetPixels(), FaceOrder[slot]);

            UnityEngine.Object.Destroy(faceTex);

        }



        cubemap.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        cubemap.filterMode = FilterMode.Bilinear;

        CubemapCache[cacheKey] = cubemap;

        return cubemap;

    }



    public static Texture2D? LoadPanorama(string setId, bool mainPoolOnly = true)

    {

        if (string.IsNullOrEmpty(setId))

        {

            return null;

        }



        if (TextureCache.TryGetValue(setId, out var cached))

        {

            return cached;

        }



        var setDir = ResolveSetDirectory(setId, mainPoolOnly);

        if (setDir == null)

        {

            return null;

        }



        var path = Path.Combine(setDir, EquirectFile);

        if (!File.Exists(path))

        {

            path = Path.Combine(setDir, LegacyPanoramaFile);

        }



        if (!File.Exists(path))

        {

            Debug.LogWarning("TopDog: combat background missing in " + setDir);

            return null;

        }



        var bytes = File.ReadAllBytes(path);

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        if (!tex.LoadImage(bytes))

        {

            UnityEngine.Object.Destroy(tex);

            return null;

        }



        tex.filterMode = FilterMode.Bilinear;

        tex.wrapMode = TextureWrapMode.Repeat;

        tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        TextureCache[setId] = tex;

        return tex;

    }



    public static bool IsMainSet(string setId) =>

        Array.IndexOf(MainSetIds, setId) >= 0;



    private static string? ResolveSetDirectory(string setId, bool mainPoolOnly)

    {

        var pool = mainPoolOnly ? "Main" : ResolvePool(setId);

        if (pool == null)

        {

            return null;

        }



        var setDir = Path.Combine(Application.dataPath, "Art", "CombatBackgrounds", pool, setId);

        return Directory.Exists(setDir) ? setDir : null;

    }



    private static string? ResolvePool(string setId)

    {

        if (Array.IndexOf(MainSetIds, setId) >= 0)

        {

            return "Main";

        }



        if (Array.IndexOf(ReserveSetIds, setId) >= 0)

        {

            return "Reserve";

        }



        return null;

    }



    private static string[]? ResolveFacePaths(string setDir)

    {

        var pngFiles = Directory.GetFiles(setDir, "*.png");

        var paths = new string[FaceSuffixes.Length];

        for (var i = 0; i < FaceSuffixes.Length; i++)

        {

            var match = FindFacePath(pngFiles, FaceSuffixes[i], FaceKeywordFallbacks[i]);

            if (match == null)

            {

                return null;

            }



            paths[i] = match;

        }



        return paths;

    }



    private static string? FindFacePath(string[] pngFiles, string axisSuffix, string[] keywordFallbacks)

    {

        foreach (var file in pngFiles)

        {

            var name = Path.GetFileName(file);

            if (name.Equals(EquirectFile, StringComparison.OrdinalIgnoreCase)

                || name.Equals(LegacyPanoramaFile, StringComparison.OrdinalIgnoreCase))

            {

                continue;

            }



            if (name.EndsWith(axisSuffix + ".png", StringComparison.OrdinalIgnoreCase))

            {

                return file;

            }

        }



        var upperKeywords = new string[keywordFallbacks.Length];

        for (var i = 0; i < keywordFallbacks.Length; i++)

        {

            upperKeywords[i] = keywordFallbacks[i].ToUpperInvariant();

        }



        foreach (var file in pngFiles)

        {

            var name = Path.GetFileName(file);

            if (name.Equals(EquirectFile, StringComparison.OrdinalIgnoreCase)

                || name.Equals(LegacyPanoramaFile, StringComparison.OrdinalIgnoreCase))

            {

                continue;

            }



            var upper = name.ToUpperInvariant();

            foreach (var keyword in upperKeywords)

            {

                if (upper.Contains(keyword))

                {

                    return file;

                }

            }

        }



        return null;

    }



    private static int ReadFaceSize(string path)

    {

        var bytes = File.ReadAllBytes(path);

        var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        if (!probe.LoadImage(bytes))

        {

            UnityEngine.Object.Destroy(probe);

            return -1;

        }



        var size = probe.width;

        UnityEngine.Object.Destroy(probe);

        return size;

    }



    private static Texture2D? LoadFaceTexture(string path)

    {

        var bytes = File.ReadAllBytes(path);

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        if (!tex.LoadImage(bytes))

        {

            UnityEngine.Object.Destroy(tex);

            return null;

        }



        tex.filterMode = FilterMode.Bilinear;

        tex.wrapMode = TextureWrapMode.Clamp;

        return tex;

    }

}


