using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Save;

namespace ZeroCompany.App.ViewModels;

/// <summary>An operator's picture, or an initials badge when the save has none.</summary>
public sealed class FaceViewModel
{
    public FaceViewModel(string name, Bitmap? image)
    {
        Name = name; Image = image;
        Initials = InitialsOf(name);
    }

    public string Name { get; }
    public Bitmap? Image { get; }
    public bool HasImage => Image != null;
    public bool NoImage => Image == null;
    public string Initials { get; }

    public static string InitialsOf(string name)
    {
        var parts = name.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }
}

/// <summary>
/// A button that queues (and un-queues) one pending action: "Heal" / "Queued - undo". It follows the pending store, so it stays
/// right when the action is cleared elsewhere (Discard, applying).
/// </summary>
public sealed partial class ToggleActionViewModel : ViewModelBase, IDisposable
{
    readonly PendingChanges _pending;
    readonly Func<bool> _isOn;
    readonly Action _toggle;
    readonly string _offLabel, _onLabel;

    public ToggleActionViewModel(PendingChanges pending, Func<bool> isOn, Action toggle, string offLabel, string onLabel = "Queued - undo", string tooltip = "")
    {
        _pending = pending; _isOn = isOn; _toggle = toggle; _offLabel = offLabel; _onLabel = onLabel; Tooltip = tooltip;
        _pending.Changed += OnChanged;
        Command = new RelayCommand(_toggle);
    }

    public System.Windows.Input.ICommand Command { get; }
    public string Tooltip { get; }
    public bool IsOn => _isOn();
    public bool IsOff => !_isOn();
    public string Label => _isOn() ? _onLabel : _offLabel;

    void OnChanged()
    {
        OnPropertyChanged(nameof(IsOn)); OnPropertyChanged(nameof(IsOff)); OnPropertyChanged(nameof(Label));
    }

    public void Dispose() => _pending.Changed -= OnChanged;
}

/// <summary>A tab button (Armory categories, Personnel tabs ...).</summary>
public sealed partial class TabViewModel : ViewModelBase
{
    public TabViewModel(string key, string label, Action<string> select)
    {
        Key = key; Label = label;
        Command = new RelayCommand(() => select(key));
    }

    public string Key { get; }
    [ObservableProperty] string _label;
    [ObservableProperty] bool _isActive;
    public System.Windows.Input.ICommand Command { get; }
}
