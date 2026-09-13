using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace VAI.RLTP;

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public class PreSptEntry(ModManager manager) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        manager.PreSptLoad();
        return Task.CompletedTask;
    }
}

[Injectable(TypePriority = OnLoadOrder.Preload + 2)]
public class PostDbEntry(ModManager manager) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        manager.PostDbLoad();
        return Task.CompletedTask;
    }
}

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class PostSptEntry(ModManager manager) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        manager.PostSptLoad();
        return Task.CompletedTask;
    }
}
