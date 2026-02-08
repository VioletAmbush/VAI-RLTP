using System.Diagnostics;
using System.Linq;
using SPTarkov.DI.Annotations;
using VAI.RLTP.Managers;

namespace VAI.RLTP;

[Injectable(InjectionType.Singleton)]
public sealed class ModManager
{
    private readonly List<AbstractModManager> _managers = [];

    public ModManager(
        GlobalsManager globalsManager,
        ContainersManager containersManager,
        DeathManager deathManager,
        WipeManager wipeManager,
        StartManager startManager,
        HideoutManager hideoutManager,
        TradersManager tradersManager,
        PresetsManager presetsManager,
        QuestsManager questsManager,
        WeaponsManager weaponsManager,
        HealingManager healingManager,
        ProfilesManager profilesManager,
        ItemsManager itemsManager,
        LocaleManager localeManager,
        BotsManager botsManager,
        ModContext _)
    {
        _managers.Add(globalsManager);
        _managers.Add(containersManager);
        _managers.Add(deathManager);
        _managers.Add(wipeManager);
        _managers.Add(startManager);
        _managers.Add(hideoutManager);
        _managers.Add(tradersManager);
        _managers.Add(presetsManager);
        _managers.Add(questsManager);
        _managers.Add(weaponsManager);
        _managers.Add(healingManager);
        _managers.Add(profilesManager);
        _managers.Add(itemsManager);
        _managers.Add(localeManager);
        _managers.Add(botsManager);

        var ordered = _managers
            .Select((manager, index) => (manager, index))
            .OrderBy(entry => entry.manager.Priority)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.manager)
            .ToList();

        _managers.Clear();
        _managers.AddRange(ordered);
    }

    public void PreSptLoad()
    {
        foreach (var manager in _managers)
        {
            manager.PreSptLoad();
        }
    }

    public void PostDbLoad()
    {
        foreach (var manager in _managers)
        {
#if DEBUG
            var managerName = manager.GetType().Name;
            Constants.GetLogger().Info($"{Constants.ModTitle}: PostDB start {managerName}");
            var sw = Stopwatch.StartNew();
#endif
            manager.PostDbLoad();
#if DEBUG
            sw.Stop();
            Constants.GetLogger().Info($"{Constants.ModTitle}: PostDB end {managerName} ({sw.ElapsedMilliseconds}ms)");
#endif
        }
    }

    public void PostSptLoad()
    {
        foreach (var manager in _managers)
        {
            manager.PostSptLoad();
        }
    }
}
