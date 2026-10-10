namespace ZeroCompany.App.Services;

/// <summary>Things only the window can do, kept out of the view-models so they stay testable.</summary>
public interface IUiServices
{
    Task<string?> PickFolderAsync(string title);
    Task<string?> PickFileAsync(string title, string label, params string[] patterns);
    Task<bool> ConfirmAsync(string title, string message);
    Task CopyToClipboardAsync(string text);
}

/// <summary>A no-op implementation for headless runs and tests (confirms everything, picks nothing).</summary>
public sealed class NullUiServices : IUiServices
{
    public bool ConfirmResult { get; set; } = true;
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    public Task<string?> PickFileAsync(string title, string label, params string[] patterns) => Task.FromResult<string?>(null);
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(ConfirmResult);
    public Task CopyToClipboardAsync(string text) => Task.CompletedTask;
}
