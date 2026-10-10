using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using ZeroCompany.App.Services;
using ZeroCompany.App.ViewModels;
using ZeroCompany.App.Views;
using ZeroCompany.Core;
using ZeroCompany.Core.Platform;
using ZeroCompany.GameData.Distill;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ZeroCompany.App.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public static class Headless
{
    static HeadlessUnitTestSession? _session;
    static readonly object Gate = new();

    public static HeadlessUnitTestSession Session { get { lock (Gate) return _session ??= HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder)); } }

    public static Task RunAsync(Func<Task> f) => Session.Dispatch(async () => { await f(); return 0; }, CancellationToken.None);
    public static void Run(Action a) => Session.Dispatch(() => { a(); return 0; }, CancellationToken.None).GetAwaiter().GetResult();
    public static T Run<T>(Func<T> f) => Session.Dispatch(f, CancellationToken.None).GetAwaiter().GetResult();
}

public static class Repo
{
    public static string Root { get; } = FindRoot();
    static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "ZeroCompany.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    public static string? SampleSave()
    {
        var dir = Path.Combine(Root, "saves");
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "HUB_Root*.sav").OrderBy(x => x).FirstOrDefault() : null;
    }

    public static string ShotsDir { get { var d = Path.Combine(Root, "artifacts", "shots"); Directory.CreateDirectory(d); return d; } }
}

/// <summary>An isolated editor: temp data folder, a copy of the sample save, and the game database built from the local dumps.</summary>
public sealed class TestRig : IDisposable
{
    public string Tmp { get; } = Path.Combine(Path.GetTempPath(), "zc-ui-" + Guid.NewGuid().ToString("N"));
    public EditorSession Session { get; }
    public MainViewModel Main { get; private set; } = null!;
    public MainWindow Window { get; private set; } = null!;
    public NullUiServices Ui { get; } = new();
    public string SaveName { get; }

    public TestRig(bool withGameData = true)
    {
        var env = new AppEnvironment(Path.Combine(Tmp, "data")) { UseSystemSaveDirs = false };
        var saves = Path.Combine(Tmp, "saves");
        Directory.CreateDirectory(saves);
        var src = Repo.SampleSave() ?? throw new InvalidOperationException("no sample save");
        SaveName = Path.GetFileName(src);
        File.Copy(src, Path.Combine(saves, SaveName));
        if (withGameData)
        {
            var raw = RawGameData.LoadLegacyDirectory(Path.Combine(Repo.Root, "gamedata"));
            if (raw != null) Distiller.Distill(raw).Save(env.GameDbPath);
        }
        var settings = new AppSettings { SaveDirs = { saves } };
        settings.Save(env);
        Session = new EditorSession(env, () => null);
    }

    /// <summary>Create the window (on the UI thread) and optionally open the sample save.</summary>
    public void Start(bool openSave = true, int width = 1280, int height = 860, bool portraits = true)
    {
        Headless.Run(() =>
        {
            Main = new MainViewModel(Session);
            Main.Portraits.Enabled = portraits;
            Window = new MainWindow { DataContext = Main, Width = width, Height = height };
            Main.Ui = Ui;
            Window.Show();
            if (openSave)
            {
                var entry = Session.Service.ListSaves().First();
                Main.OpenSave(entry);
            }
            Pump();
        });
    }

    public void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    public string Shot(string name)
    {
        var path = Path.Combine(Repo.ShotsDir, name + ".png");
        Headless.Run(() =>
        {
            Pump();
            var size = new PixelSize((int)Window.Bounds.Width, (int)Window.Bounds.Height);
            using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
            rtb.Render(Window);                      // draws the current visual tree directly
            rtb.Save(path);
        });
        return path;
    }

    public void Dispose()
    {
        Headless.Run(() => { Window?.Close(); });
        try { Directory.Delete(Tmp, true); } catch (IOException) { }
    }
}
