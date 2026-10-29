using System.Runtime.InteropServices;
using Avalonia;

namespace ZeroCompany.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (!NativeLibrariesPresent()) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    /// <summary>
    /// Release builds load the native libraries (Skia first) from the editor's own folder, so an exe copied out on its own would
    /// crash before any window opens. Say what is wrong instead. Development builds keep them under runtimes/ and are not checked.
    /// </summary>
    static bool NativeLibrariesPresent()
    {
        var dir = AppContext.BaseDirectory;
        if (Directory.Exists(Path.Combine(dir, "runtimes"))) return true;
        var skia = OperatingSystem.IsWindows() ? "libSkiaSharp.dll" : OperatingSystem.IsMacOS() ? "libSkiaSharp.dylib" : "libSkiaSharp.so";
        if (File.Exists(Path.Combine(dir, skia))) return true;

        var message = $"The editor can't start: {skia} and its other library files are missing from this folder.\n\n" +
                      "Unpack the whole download into one folder and run the editor from there.";
        if (OperatingSystem.IsWindows()) MessageBoxW(0, message, "Zero Company Editor", 0x10 /* MB_ICONERROR */);
        else Console.Error.WriteLine(message);
        Environment.ExitCode = 1;
        return false;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);
}
