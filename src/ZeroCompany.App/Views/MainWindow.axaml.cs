using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using ZeroCompany.App.Services;
using ZeroCompany.App.ViewModels;

namespace ZeroCompany.App.Views;

public partial class MainWindow : Window, IUiServices
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm) vm.Ui = this;
        };
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return r.Count > 0 ? r[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFileAsync(string title, string label, params string[] patterns)
    {
        var r = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(label) { Patterns = patterns } },
        });
        return r.Count > 0 ? r[0].TryGetLocalPath() : null;
    }

    public async Task CopyToClipboardAsync(string text)
    {
        if (Clipboard != null) await Clipboard.SetTextAsync(text);
    }

    public async Task OpenUrlAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            await Launcher.LaunchUriAsync(uri);
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var dlg = new Window
        {
            Title = title, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        bool result = false;
        var yes = new Button { Content = "OK", Classes = { "primary" }, MinWidth = 90 };
        var no = new Button { Content = "Cancel", MinWidth = 90 };
        yes.Click += (_, _) => { result = true; dlg.Close(); };
        no.Click += (_, _) => dlg.Close();
        dlg.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20), Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { no, yes },
                },
            },
        };
        await dlg.ShowDialog(this);
        return result;
    }
}
