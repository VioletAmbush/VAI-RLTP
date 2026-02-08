using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

namespace VAI.RLTP.Managers;

internal static class LocaleExtensionDataAdapter
{
    public static bool TryGetServerLocales(
        IDictionary<string, object>? extensionData,
        JsonUtil jsonUtil,
        out string key,
        out Dictionary<string, LazyLoad<Dictionary<string, string>>> serverLocales,
        out bool hasServerPayload)
    {
        key = string.Empty;
        serverLocales = null!;
        hasServerPayload = false;

        if (extensionData is null)
        {
            return false;
        }

        if (!TryGetServerPayload(extensionData, out key, out var payload))
        {
            return false;
        }

        hasServerPayload = true;

        if (payload is Dictionary<string, LazyLoad<Dictionary<string, string>>> typed && typed.Count > 0)
        {
            serverLocales = typed;
            return true;
        }

        if (!TrySerializePayload(payload, out var payloadJson))
        {
            return false;
        }

        var deserialized = jsonUtil.Deserialize<Dictionary<string, LazyLoad<Dictionary<string, string>>>>(payloadJson);
        if (deserialized is null || deserialized.Count == 0)
        {
            return false;
        }

        serverLocales = deserialized;
        return true;
    }

    private static bool TryGetServerPayload(
        IDictionary<string, object> extensionData,
        out string key,
        out object? payload)
    {
        if (extensionData.TryGetValue("server", out payload))
        {
            key = "server";
            return true;
        }

        if (extensionData.TryGetValue("Server", out payload))
        {
            key = "Server";
            return true;
        }

        key = string.Empty;
        payload = null;
        return false;
    }

    private static bool TrySerializePayload<TPayload>(TPayload payload, out string json)
    {
        json = string.Empty;
        if (payload is null)
        {
            return false;
        }

        try
        {
            json = payload switch
            {
                JsonNode node => node.ToJsonString(),
                JsonElement element => element.GetRawText(),
                string text => text,
                _ => JsonSerializer.Serialize(payload)
            };
        }
        catch
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(json);
    }
}
