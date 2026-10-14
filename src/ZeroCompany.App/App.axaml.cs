using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ZeroCompany.App.Services;
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
            var main = new MainViewModel(session);
            desktop.MainWindow = new MainWindow { DataContext = main };
            // Last line of defence: an exception no screen caught is logged and shown instead of closing the editor.
            Dispatcher.UIThread.UnhandledException += (_, e) => { main.ReportUnexpected("Something went wrong", e.Exception); e.Handled = true; };
            TaskScheduler.UnobservedTaskException += (_, e) => { CrashLog.Write(session.Env.DataDir, e.Exception); e.SetObserved(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
