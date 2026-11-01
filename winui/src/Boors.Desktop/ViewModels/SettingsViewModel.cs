using Boors.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Boors.Desktop.ViewModels;

/// <summary>Appearance, data source, data folder and background download. Only the data folder and node name need a restart.</summary>
public sealed partial class SettingsViewModel(SettingsService settings) : ObservableObject
{
    public AppSettings Current => settings.Current;

    public string EffectiveDataRoot => settings.EffectiveDataRoot;

    public bool DataRootOverridden => settings.DataRootOverridden;

    public string EffectiveNode => settings.EffectiveNode;

    [ObservableProperty]
    public partial bool RestartPending { get; set; }

    public static IReadOnlyList<Choice> Languages => [new("fa", "فارسی"), new("en", "English")];

    public static IReadOnlyList<Choice> Themes => [new("Dark", Loc.T("Dark")), new("Light", Loc.T("Light")), new("System", Loc.T("Use system setting"))];

    public static IReadOnlyList<Choice> DigitChoices => [new("Persian", Loc.T("Persian digits (۱۲۳)")), new("Latin", Loc.T("Latin digits (123)"))];

    public static IReadOnlyList<Choice> Presets => [new("Iranian", Loc.T("Green up, red down (Iranian)")), new("ColorBlind", Loc.T("Blue up, orange down (colour-blind safe)"))];

    public static IReadOnlyList<Choice> Densities => [new("Pro", Loc.T("Pro: every panel, compact rows")), new("Lite", Loc.T("Lite: essentials, larger rows"))];

    public static IReadOnlyList<Choice> Modes => [new("Local", Loc.T("This computer (offline replica)")), new("Remote", Loc.T("A Boors node over HTTP"))];

    public void Set(Action<AppSettings> change)
    {
        settings.Update(change);
        OnPropertyChanged(nameof(Current));
    }

    public void SetDataRoot(string? path)
    {
        Set(s => s.DataRoot = string.IsNullOrWhiteSpace(path) ? null : path.Trim());
        RestartPending = true;
    }

    public void SetAutoDownload(bool on) => Set(s => s.AutoDownload = on);

    public void SetNodeName(string? name)
    {
        Set(s => s.NodeName = string.IsNullOrWhiteSpace(name) ? null : name.Trim());
        RestartPending = true;
    }
}
