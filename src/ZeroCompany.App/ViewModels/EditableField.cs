using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

/// <summary>
/// One editable number, bound to the pending-changes store. Values are shown in game units (<c>offset</c> converts: LV = save + 1)
/// and written in save units. An edit equal to the saved value is no edit at all.
/// </summary>
public sealed class EditableField : ObservableObject, IDisposable
{
    readonly PendingChanges _pending;
    string _text;
    bool _invalid;

    public FieldRef Ref { get; }
    public int Offset { get; }
    public string Label { get; }

    public EditableField(FieldRef r, PendingChanges pending, int offset = 0, string label = "")
    {
        Ref = r; _pending = pending; Offset = offset; Label = label;
        _text = Format(Value);
        _pending.Changed += OnPendingChanged;
    }

    public double Min => Ref.Min + Offset;
    public double Max => Ref.Max + Offset;
    public bool IsFloat => Ref.Kind == "float";
    public double Original => Ref.Value + Offset;
    public bool IsChanged => _pending.IsChanged(Ref);
    public bool IsInvalid => _invalid;

    /// <summary>Current value in game units (pending edit, else the saved value).</summary>
    public double Value
    {
        get => _pending.GetValue(Ref) + Offset;
        set => Commit(value);
    }

    public int IntValue => (int)Math.Round(Value);

    /// <summary>Typed text; applied as soon as it is a valid number inside the field's limits.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            OnPropertyChanged();
            var cleaned = (value ?? "").Replace(",", "").Replace(" ", "");
            if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && PendingChanges.Valid(Ref, v - Offset))
            {
                SetInvalid(false);
                if (v != Value) Commit(v);
            }
            else SetInvalid(true);
        }
    }

    void Commit(double displayValue)
    {
        var save = displayValue - Offset;
        if (!PendingChanges.Valid(Ref, save)) { SetInvalid(true); return; }
        SetInvalid(false);
        _pending.SetEdit(Ref, save);
    }

    /// <summary>Move by whole steps, clamped to the limits (for steppers and pips).</summary>
    public void Step(int delta) => Commit(Math.Clamp(Value + delta, Min, Max));

    public void SetTo(double displayValue) => Commit(Math.Clamp(displayValue, Min, Max));

    public void Revert() => _pending.Revert(Ref);

    void SetInvalid(bool v)
    {
        if (_invalid == v) return;
        _invalid = v;
        OnPropertyChanged(nameof(IsInvalid));
    }

    string Format(double v) => IsFloat ? v.ToString("0.####", CultureInfo.InvariantCulture) : ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture);

    void OnPendingChanged()
    {
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IntValue));
        // Resync the text unless the user is mid-typing something that is not (yet) a valid number.
        var cleaned = (_text ?? "").Replace(",", "").Replace(" ", "");
        bool parsesToCurrent = double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && Math.Abs(p - Value) < 1e-9;
        if (!parsesToCurrent && !_invalid)
        {
            _text = Format(Value);
            OnPropertyChanged(nameof(Text));
        }
    }

    public void Dispose() => _pending.Changed -= OnPendingChanged;
}

/// <summary>Makes <see cref="EditableField"/>s and disposes them all together when a screen goes away.</summary>
public sealed class FieldSet : IDisposable
{
    readonly PendingChanges _pending;
    readonly List<EditableField> _all = new();

    public FieldSet(PendingChanges pending) { _pending = pending; }

    public EditableField? Make(FieldRef? r, int offset = 0, string label = "")
    {
        if (r == null) return null;
        var f = new EditableField(r, _pending, offset, label);
        _all.Add(f);
        return f;
    }

    public void Dispose()
    {
        foreach (var f in _all) f.Dispose();
        _all.Clear();
    }
}
