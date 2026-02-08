using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace VAI.RLTP;

[Injectable(TypePriority = OnLoadOrder.PreSptModLoader + 1)]
public class PreSptEntry(ModManager manager) : IOnLoad
{
    public Task OnLoad()
    {
        manager.PreSptLoad();
        return Task.CompletedTask;
    }
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class PostDbEntry(ModManager manager) : IOnLoad
{
    public Task OnLoad()
    {
        manager.PostDbLoad();
        return Task.CompletedTask;
    }
}

[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 1)]
public class PostSptEntry(ModManager manager) : IOnLoad
{
    public Task OnLoad()
    {
        manager.PostSptLoad();
        return Task.CompletedTask;
    }
}
