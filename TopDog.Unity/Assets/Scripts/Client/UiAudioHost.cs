using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TopDog.Client;

/// <summary>Background music (folder scan + shuffled playlist) and UI click feedback.</summary>
public sealed class UiAudioHost : MonoBehaviour
{
    private const float ClickClipSeconds = 0.05f;
    private const float ClickFrequencyHz = 1200f;

    private static UiAudioHost? _instance;

    private AudioSource? _bgmSource;
    private AudioSource? _sfxSource;
    private AudioClip[] _bgmClips = Array.Empty<AudioClip>();
    private AudioClip? _clickClip;
    private readonly List<int> _playOrder = new();
    private int _playOrderIndex;
    private int _lastPlayedClipIndex = -1;
    private readonly System.Random _shuffleRng = new();
    private readonly HashSet<UIDocument> _wiredDocuments = new();
    private bool _bgmReady;
    private Coroutine? _loadRoutine;

    public static UiAudioHost? Instance => _instance;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        if (FindAnyObjectByType<AudioListener>() == null)
        {
            gameObject.AddComponent<AudioListener>();
        }

        _bgmSource = gameObject.AddComponent<AudioSource>();
        _bgmSource.loop = false;
        _bgmSource.playOnAwake = false;

        _sfxSource = gameObject.AddComponent<AudioSource>();
        _sfxSource.loop = false;
        _sfxSource.playOnAwake = false;

        _clickClip = BuildClickClip();
        ClientGameSettings.AudioSettingsChanged += ApplySettings;
        ApplySettings();
    }

    private void Start()
    {
        _loadRoutine = StartCoroutine(LoadBackgroundMusicRoutine());
    }

    private void OnDestroy()
    {
        ClientGameSettings.AudioSettingsChanged -= ApplySettings;
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private IEnumerator LoadBackgroundMusicRoutine()
    {
        _bgmReady = false;
        yield return BackgroundMusicLoader.LoadAllClips(clips =>
        {
            _bgmClips = new AudioClip[clips.Count];
            for (var i = 0; i < clips.Count; i++)
            {
                _bgmClips[i] = clips[i];
            }
        });

        _bgmReady = true;
        ApplySettings();
        _loadRoutine = null;
    }

    private void Update()
    {
        if (!_bgmReady || _bgmSource == null || _bgmClips.Length == 0)
        {
            return;
        }

        if (!ClientGameSettings.BackgroundMusicEnabled)
        {
            if (_bgmSource.isPlaying)
            {
                _bgmSource.Stop();
            }

            return;
        }

        if (!_bgmSource.isPlaying && _bgmSource.clip != null)
        {
            PlayNextInPlaylist();
        }
    }

    public static void Ensure()
    {
        if (_instance != null)
        {
            return;
        }

        var host = GameAppHost.Instance;
        if (host != null)
        {
            if (host.GetComponent<UiAudioHost>() == null)
            {
                host.gameObject.AddComponent<UiAudioHost>();
            }

            return;
        }

        var go = new GameObject("UiAudioHost");
        go.AddComponent<UiAudioHost>();
    }

    public void RegisterDocument(UIDocument document)
    {
        if (document == null || _wiredDocuments.Contains(document))
        {
            return;
        }

        void Wire()
        {
            var root = document.rootVisualElement;
            if (root == null)
            {
                return;
            }

            if (_wiredDocuments.Contains(document))
            {
                return;
            }

            root.RegisterCallback<ClickEvent>(OnUiClick);
            _wiredDocuments.Add(document);
        }

        Wire();
        if (document.rootVisualElement != null)
        {
            document.rootVisualElement.RegisterCallback<AttachToPanelEvent>(_ => Wire());
        }
    }

    private void OnUiClick(ClickEvent evt)
    {
        if (!ClientGameSettings.UiClickSoundEnabled)
        {
            return;
        }

        if (evt.target is not VisualElement ve)
        {
            return;
        }

        for (var node = ve; node != null; node = node.parent)
        {
            if (node is Button)
            {
                PlayClick();
                return;
            }
        }
    }

    public void PlayClick()
    {
        if (_sfxSource == null || _clickClip == null || !ClientGameSettings.UiClickSoundEnabled)
        {
            return;
        }

        _sfxSource.PlayOneShot(_clickClip, ClientGameSettings.MasterVolume);
    }

    private void ApplySettings()
    {
        var volume = ClientGameSettings.MasterVolume;
        if (_bgmSource != null)
        {
            _bgmSource.volume = volume;
        }

        if (_sfxSource != null)
        {
            _sfxSource.volume = volume;
        }

        if (!ClientGameSettings.BackgroundMusicEnabled)
        {
            _bgmSource?.Stop();
            return;
        }

        if (_bgmReady && _bgmClips.Length > 0 && _bgmSource != null && !_bgmSource.isPlaying)
        {
            StartPlaylist();
        }
    }

    private void StartPlaylist()
    {
        if (_bgmClips.Length == 0 || _bgmSource == null)
        {
            return;
        }

        RebuildShuffle(_lastPlayedClipIndex);
        _playOrderIndex = 0;
        PlayClipAtOrderIndex();
    }

    private void PlayNextInPlaylist()
    {
        if (_bgmClips.Length == 0 || _bgmSource == null)
        {
            return;
        }

        _playOrderIndex++;
        if (_playOrderIndex >= _playOrder.Count)
        {
            RebuildShuffle(_lastPlayedClipIndex);
            _playOrderIndex = 0;
        }

        PlayClipAtOrderIndex();
    }

    private void PlayClipAtOrderIndex()
    {
        if (_bgmSource == null || _playOrder.Count == 0)
        {
            return;
        }

        var clipIndex = _playOrder[_playOrderIndex];
        _lastPlayedClipIndex = clipIndex;
        _bgmSource.clip = _bgmClips[clipIndex];
        _bgmSource.volume = ClientGameSettings.MasterVolume;
        _bgmSource.Play();
    }

    private void RebuildShuffle(int avoidFirstClipIndex)
    {
        _playOrder.Clear();
        for (var i = 0; i < _bgmClips.Length; i++)
        {
            _playOrder.Add(i);
        }

        for (var i = _playOrder.Count - 1; i > 0; i--)
        {
            var j = _shuffleRng.Next(i + 1);
            (_playOrder[i], _playOrder[j]) = (_playOrder[j], _playOrder[i]);
        }

        if (_bgmClips.Length > 1
            && avoidFirstClipIndex >= 0
            && _playOrder[0] == avoidFirstClipIndex)
        {
            (_playOrder[0], _playOrder[1]) = (_playOrder[1], _playOrder[0]);
        }
    }

    private static AudioClip BuildClickClip()
    {
        const int sampleRate = 44100;
        var sampleCount = Mathf.Max(1, Mathf.RoundToInt(sampleRate * ClickClipSeconds));
        var data = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (float)sampleRate;
            var envelope = Mathf.Exp(-t * 90f);
            data[i] = Mathf.Sin(2f * Mathf.PI * ClickFrequencyHz * t) * envelope * 0.35f;
        }

        var clip = AudioClip.Create("ui_click", sampleCount, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
