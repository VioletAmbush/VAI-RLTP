using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using VAI.RLTP.Managers;

namespace VAI.RLTP.Routers;

[Injectable]
public sealed class DeathRoutes(JsonUtil jsonUtil, DeathRouteCallbacks callbacks)
    : StaticRouter(jsonUtil,
    [
        new RouteAction<RaidEndRequestData>(
            "/client/match/local/end",
            async (url, info, sessionId, output) => await callbacks.HandleRaidEnd(url, info, sessionId, output)
        ),
        new RouteAction<EmptyRequestData>(
            "/client/ragfair/find",
            async (url, info, sessionId, output) => await callbacks.HandleRagfairFind(url, sessionId, output)
        )
    ])
{ }

[Injectable]
public sealed class DeathRouteCallbacks(DeathManager deathManager)
{
    private readonly DeathManager _deathManager = deathManager;

    public async ValueTask<string> HandleRaidEnd(string url, RaidEndRequestData info, MongoId sessionId, string output)
    {
        await _deathManager.HandleRaidEnd(info, sessionId);
        return output;
    }

    public ValueTask<string> HandleRagfairFind(string url, MongoId sessionId, string output)
    {
        _deathManager.HandleRagfairFind(sessionId);
        return new ValueTask<string>(output);
    }
}

public sealed record RaidEndRequestData : IRequestData
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = new();
}
