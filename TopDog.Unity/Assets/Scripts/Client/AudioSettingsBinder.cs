using UnityEngine;
using UnityEngine.UIElements;

namespace TopDog.Client;

/// <summary>Audio toggles + master volume in Settings / pause overlay. Changes apply immediately.</summary>
public sealed class AudioSettingsBinder
{
    public const string BgmToggleName = "toggle-bgm";
    public const string UiClickToggleName = "toggle-ui-click";
    public const string VolumeSliderName = "slider-master-volume";
    public const string VolumeValueLabelName = "lbl-master-volume-value";

    private Toggle? _bgmToggle;
    private Toggle? _uiClickToggle;
    private Slider? _volumeSlider;
    private Label? _volumeLabel;

    public static VisualElement BuildPauseSettingsBlock(AudioSettingsBinder binder)
    {
        var block = new VisualElement();
        block.AddToClassList("settings-options");
        block.Add(binder.BuildBgmToggleRow());
        block.Add(binder.BuildUiClickToggleRow());
        block.Add(binder.BuildVolumeRow());
        binder.Bind(block);
        return block;
    }

    public void Bind(VisualElement container)
    {
        _bgmToggle = container.Q<Toggle>(BgmToggleName);
        _uiClickToggle = container.Q<Toggle>(UiClickToggleName);
        _volumeSlider = container.Q<Slider>(VolumeSliderName);
        _volumeLabel = container.Q<Label>(VolumeValueLabelName);
        LoadFromSaved();
        WireControls();
    }

    public void LoadFromSaved()
    {
        if (_bgmToggle != null)
        {
            _bgmToggle.SetValueWithoutNotify(ClientGameSettings.BackgroundMusicEnabled);
        }

        if (_uiClickToggle != null)
        {
            _uiClickToggle.SetValueWithoutNotify(ClientGameSettings.UiClickSoundEnabled);
        }

        if (_volumeSlider != null)
        {
            _volumeSlider.SetValueWithoutNotify(ClientGameSettings.MasterVolume * 100f);
            if (_volumeLabel != null)
            {
                _volumeLabel.text = FormatVolume(_volumeSlider.value);
            }
        }
    }

    private void WireControls()
    {
        if (_bgmToggle != null)
        {
            _bgmToggle.RegisterValueChangedCallback(evt =>
                ClientGameSettings.SetBackgroundMusicEnabled(evt.newValue));
        }

        if (_uiClickToggle != null)
        {
            _uiClickToggle.RegisterValueChangedCallback(evt =>
                ClientGameSettings.SetUiClickSoundEnabled(evt.newValue));
        }

        if (_volumeSlider != null && _volumeLabel != null)
        {
            _volumeSlider.RegisterValueChangedCallback(evt =>
            {
                _volumeLabel.text = FormatVolume(evt.newValue);
                ClientGameSettings.SetMasterVolume(evt.newValue / 100f);
            });
        }
    }

    public VisualElement BuildBgmToggleRow() => BuildToggleRow("背景音乐", BgmToggleName, false);

    public VisualElement BuildUiClickToggleRow() => BuildToggleRow("鼠标点击音效", UiClickToggleName, true);

    public VisualElement BuildVolumeRow() => BuildVolumeSliderRow(
        "音量",
        VolumeValueLabelName,
        VolumeSliderName,
        ClientGameSettings.MinMasterVolume * 100f,
        ClientGameSettings.MaxMasterVolume * 100f,
        ClientGameSettings.DefaultMasterVolume * 100f);

    private static VisualElement BuildToggleRow(string title, string toggleName, bool defaultValue)
    {
        var row = new VisualElement();
        row.AddToClassList("settings-row");

        var titleLabel = new Label(title);
        titleLabel.AddToClassList("settings-label");
        row.Add(titleLabel);

        var toggle = new Toggle { name = toggleName, value = defaultValue };
        toggle.AddToClassList("settings-toggle");
        row.Add(toggle);
        return row;
    }

    private static VisualElement BuildVolumeSliderRow(
        string title,
        string valueLabelName,
        string sliderName,
        float low,
        float high,
        float defaultValue)
    {
        var row = new VisualElement();
        row.AddToClassList("settings-row");

        var titleLabel = new Label(title);
        titleLabel.AddToClassList("settings-label");
        row.Add(titleLabel);

        var valueLabel = new Label { name = valueLabelName };
        valueLabel.AddToClassList("settings-value");
        row.Add(valueLabel);

        var slider = new Slider(low, high)
        {
            name = sliderName,
            showInputField = false,
        };
        slider.SetValueWithoutNotify(defaultValue);
        slider.AddToClassList("settings-slider");
        row.Add(slider);
        return row;
    }

    private static string FormatVolume(float percent) => Mathf.RoundToInt(percent) + "%";
}
