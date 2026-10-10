using ZeroCompany.Core.Platform;
using ZeroCompany.Core.Save;
using ZeroCompany.GameData.Models;

namespace ZeroCompany.Core;

/// <summary>
/// Everything one running editor needs, wired together: where data lives, the user's settings, the extracted game database,
/// the save service and the pending changes of the open save.
/// </summary>
public sealed class EditorSession
{
    readonly Func<string?>? _gameRunning;

    public AppEnvironment Env { get; }
    public AppSettings Settings { get; }
    public GameDatabase Db { get; private set; }
    public SaveService Service { get; private set; }
    public PendingChanges Pending { get; } = new();

    public EditorSession(AppEnvironment env, Func<string?>? gameRunning = null)
    {
        Env = env;
        _gameRunning = gameRunning;
        Settings = AppSettings.Load(env);
        Db = GameDatabase.TryLoad(env.GameDbPath) ?? GameDatabase.Empty;
        Service = BuildService();
    }

    public OpenSave? Current => Service.Current;
    public bool HasGameData => !Db.IsEmpty;

    SaveService BuildService() => new(Env.DiscoverSaveDirs(Settings.SaveDirs), Env.BackupsDir, Db, _gameRunning);

    /// <summary>Re-read the save folders (after the user adds one) and the game database (after an extraction).</summary>
    public void Refresh()
    {
        Db = GameDatabase.TryLoad(Env.GameDbPath) ?? GameDatabase.Empty;
        Service = BuildService();
        Pending.Clear();
    }

    public void SaveSettings() => Settings.Save(Env);

    /// <summary>Install a freshly extracted (or imported) database and use it from now on.</summary>
    public void UseDatabase(GameDatabase db)
    {
        db.Save(Env.GameDbPath);
        Db = db;
        Service.Db = db;
    }

    public OpenSave Open(string dirId, string name)
    {
        var open = Service.Open(dirId, name);
        Pending.Clear();
        return open;
    }

    /// <summary>Write the pending changes to the save (or to a copy). A successful in-place write clears the pending set.</summary>
    public ApplyResult Apply(bool asCopy, bool force)
    {
        var cur = Service.Current ?? throw new EditException("no save open");
        var res = Service.Apply(Pending.BuildChanges(), force, asCopy, Pending.BuildActions(cur.Upgrades));
        Pending.Clear();
        return res;
    }
}
