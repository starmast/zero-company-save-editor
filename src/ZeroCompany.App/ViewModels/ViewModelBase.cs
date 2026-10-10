using CommunityToolkit.Mvvm.ComponentModel;

namespace ZeroCompany.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
}

/// <summary>A screen's view-model: disposed when the user navigates away, so it can unhook from the pending-changes store.</summary>
public abstract class ScreenViewModel : ViewModelBase, IDisposable
{
    readonly List<IDisposable> _owned = new();

    protected T Own<T>(T d) where T : IDisposable { _owned.Add(d); return d; }

    public virtual void Dispose()
    {
        foreach (var d in _owned) d.Dispose();
        _owned.Clear();
    }
}
