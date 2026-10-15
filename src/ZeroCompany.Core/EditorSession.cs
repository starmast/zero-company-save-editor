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
    public SaveService Service { get; }
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

    /// <summary>Re-read the save folders (after the user adds one) and the game database. An open save stays open.</summary>
    public void Refresh()
    {
        Db = GameDatabase.TryLoad(Env.GameDbPath) ?? GameDatabase.Empty;
        Service.Dirs = Env.DiscoverSaveDirs(Settings.SaveDirs);
        Service.Db = Db;
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
        Pending.Reset(open);
        return open;
    }

    /// <summary>
    /// Read the open save again (e.g. so a new game database names things). Pending edits are kept when the file on disk is the one
    /// they were made against, since field ids are byte offsets into it; returns false when it changed and they had to be dropped.
    /// </summary>
    public bool Reload()
    {
        var cur = Service.Current ?? throw new EditException("no save open");
        var open = Service.Open(cur.DirId, cur.Name);
        if (open.DiskHash == cur.DiskHash) { Pending.Relink(open); return true; }
        bool had = Pending.Count > 0;
        Pending.Reset(open);
        return !had;
    }

    /// <summary>
    /// Write the pending changes to the save (or to a copy). A successful in-place write clears the pending set; after a copy the
    /// open save is unchanged, so the edits stay pending.
    /// </summary>
    public ApplyResult Apply(bool asCopy, bool force, string? copyName = null)
    {
        var batch = PrepareApply();
        var res = Write(batch, asCopy, force, copyName);
        FinishApply(asCopy);
        return res;
    }

    /// <summary>The pending changes and actions as one batch. Call it on the thread that edits <see cref="Pending"/>.</summary>
    public (List<FieldChange> Changes, List<SaveAction> Actions) PrepareApply()
    {
        var cur = Service.Current ?? throw new EditException("no save open");
        var actions = Pending.BuildActions(cur.Upgrades);
        if (cur.Combat != null) actions.AddRange(Pending.BuildCombatActions(cur.Combat));
        return (Pending.BuildChanges(), actions);
    }

    /// <summary>Write a prepared batch (safe to run on a worker thread: it does not touch <see cref="Pending"/>).</summary>
    public ApplyResult Write((List<FieldChange> Changes, List<SaveAction> Actions) batch, bool asCopy, bool force, string? copyName = null) =>
        Service.Apply(batch.Changes, force, asCopy, batch.Actions, copyName);

    /// <summary>After a successful write: an in-place one re-opened the save, so the old field ids no longer apply.</summary>
    public void FinishApply(bool asCopy)
    {
        if (!asCopy) Pending.Reset(Service.Current);
    }
}
