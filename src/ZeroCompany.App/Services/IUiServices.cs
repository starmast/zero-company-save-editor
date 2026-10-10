namespace ZeroCompany.App.Services;

/// <summary>Things only the window can do, kept out of the view-models so they stay testable.</summary>
public interface IUiServices
{
    Task<string?> PickFolderAsync(string title);
    Task<string?> PickFileAsync(string title, string label, params string[] patterns);
    Task<bool> ConfirmAsync(string title, string message);
    Task CopyToClipboardAsync(string text);
    /// <summary>Open a web page in the default browser.</summary>
    Task OpenUrlAsync(string url);
}

/// <summary>A no-op implementation for headless runs and tests (confirms everything, picks nothing).</summary>
public sealed class NullUiServices : IUiServices
{
    public bool ConfirmResult { get; set; } = true;
    public string? PickedFolder { get; set; }
    public string? PickedFile { get; set; }
    public Task<string?> PickFolderAsync(string title) => Task.FromResult(PickedFolder);
    public Task<string?> PickFileAsync(string title, string label, params string[] patterns) => Task.FromResult(PickedFile);
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(ConfirmResult);
    public string? OpenedUrl { get; private set; }
    public Task CopyToClipboardAsync(string text) => Task.CompletedTask;
    public Task OpenUrlAsync(string url) { OpenedUrl = url; return Task.CompletedTask; }
}
