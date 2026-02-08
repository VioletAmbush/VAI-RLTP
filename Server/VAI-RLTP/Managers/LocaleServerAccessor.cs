using SPTarkov.Server.Core.Models.Spt.Server;
using SPTarkov.Server.Core.Utils.Json;

namespace VAI.RLTP.Managers;

internal static class LocaleServerAccessor
{
    public static Dictionary<string, LazyLoad<Dictionary<string, string>>>? GetOrNormalizeServerLocales(
        LocaleBase locales,
        string callerName)
    {
        var extensionData = locales.ExtensionData;
        if (!LocaleExtensionDataAdapter.TryGetServerLocales(
                extensionData,
                Constants.GetJsonUtil(),
                out var key,
                out var normalized,
                out var hasServerPayload))
        {
            if (hasServerPayload)
            {
                Constants.GetLogger().Warning(
                    $"{Constants.ModTitle}: {callerName} failed to parse locales extension '{key}' as server locale map.");
            }
            return null;
        }

        extensionData[key] = normalized;
        return normalized;
    }
}
