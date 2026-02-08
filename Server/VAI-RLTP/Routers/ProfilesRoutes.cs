using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using VAI.RLTP.Managers;

namespace VAI.RLTP.Routers;

[Injectable]
public sealed class ProfilesRoutes(JsonUtil jsonUtil, ProfilesRouteCallbacks callbacks)
    : StaticRouter(jsonUtil,
    [
        new RouteAction<EmptyRequestData>(
            "/client/profile/status",
            async (url, info, sessionId, output) => await callbacks.HandleProfileStatus(url, sessionId, output)
        )
    ])
{ }

[Injectable]
public sealed class ProfilesRouteCallbacks(ProfilesManager profilesManager)
{
    private readonly ProfilesManager _profilesManager = profilesManager;

    public async ValueTask<string> HandleProfileStatus(string url, MongoId sessionId, string output)
    {
        await _profilesManager.HandleProfileStatus(sessionId);
        return output;
    }
}
