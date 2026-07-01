using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace TopDog.Client;

/// <summary>
/// Scans <see cref="FolderRelativePath"/> under StreamingAssets for supported audio files.
/// Drop any .wav / .ogg / .mp3 / .aiff into that folder — no code changes required.
/// </summary>
public static class BackgroundMusicLoader
{
    public const string FolderRelativePath = "Audio/BackgroundMusic";

    private static readonly string[] SupportedExtensions =
    {
        ".wav", ".wave", ".ogg", ".mp3", ".aiff", ".aif",
    };

    public static string ResolveFolderAbsolute()
    {
        return Path.Combine(Application.streamingAssetsPath, FolderRelativePath);
    }

    public static IEnumerator LoadAllClips(Action<IReadOnlyList<AudioClip>> onComplete)
    {
        var clips = new List<AudioClip>();
        var folder = ResolveFolderAbsolute();

        if (!Directory.Exists(folder))
        {
            Debug.LogWarning(
                "TopDog: background music folder missing: " + folder
                + " — create it and add .wav/.ogg/.mp3/.aiff files.");
            onComplete(clips);
            yield break;
        }

        var files = Directory
            .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedAudioFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            Debug.LogWarning(
                "TopDog: no supported audio files in " + folder
                + " (extensions: " + string.Join(", ", SupportedExtensions) + ").");
            onComplete(clips);
            yield break;
        }

        foreach (var filePath in files)
        {
            var audioType = ResolveAudioType(filePath);
            if (audioType == null)
            {
                continue;
            }

            using var request = UnityWebRequestMultimedia.GetAudioClip(ToFileUrl(filePath), audioType.Value);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    "TopDog: skipped background track '" + Path.GetFileName(filePath)
                    + "': " + request.error);
                continue;
            }

            var clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null)
            {
                Debug.LogWarning("TopDog: skipped empty clip '" + Path.GetFileName(filePath) + "'.");
                continue;
            }

            clip.name = Path.GetFileNameWithoutExtension(filePath);
            clips.Add(clip);
        }

        Debug.Log("TopDog: loaded " + clips.Count + " background track(s) from " + folder);
        onComplete(clips);
    }

    public static bool IsSupportedAudioFile(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
        {
            return false;
        }

        return SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static AudioType? ResolveAudioType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" or ".wave" => AudioType.WAV,
            ".ogg" => AudioType.OGGVORBIS,
            ".mp3" => AudioType.MPEG,
            ".aiff" or ".aif" => AudioType.AIFF,
            _ => null,
        };
    }

    private static string ToFileUrl(string absolutePath)
    {
        var normalized = absolutePath.Replace('\\', '/');
        return normalized.Contains("://") ? normalized : "file:///" + normalized;
    }
}
