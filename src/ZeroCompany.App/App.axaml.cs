using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ZeroCompany.App.ViewModels;
using ZeroCompany.App.Views;
using ZeroCompany.Core;
using ZeroCompany.Core.Platform;

namespace ZeroCompany.App;

public sealed class App : Application
{
    /// <summary>Set by tests/tools to run against an isolated data folder instead of the user's.</summary>
    public static Func<EditorSession>? SessionFactory { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var session = SessionFactory?.Invoke() ?? new EditorSession(new AppEnvironment());
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(session) };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
