using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.Server.Core.Utils;

namespace VAI.RLTP.Routers;

internal static class ItemEventPayloadAdapter
{
    public static void AppendExtensionData(JsonObject target, IEnumerable<KeyValuePair<string?, object?>>? extensionData)
    {
        if (extensionData is null)
        {
            return;
        }

        foreach (var (key, value) in extensionData)
        {
            if (string.IsNullOrWhiteSpace(key) || target.ContainsKey(key))
            {
                continue;
            }

            target[key] = ToJsonNode(value);
        }
    }

    public static bool TryApplyLocation<TLocation>(JsonObject itemNode, TLocation location, JsonUtil jsonUtil, out string error)
    {
        error = string.Empty;
        if (location is null)
        {
            error = "profile location is null";
            return false;
        }

        if (location is JsonObject objectNode)
        {
            itemNode["location"] = objectNode.DeepClone();
            return true;
        }

        if (location is JsonNode node)
        {
            itemNode["location"] = node.DeepClone();
            return true;
        }

        if (location is JsonElement element)
        {
            try
            {
                var parsed = JsonNode.Parse(element.GetRawText());
                if (parsed is null)
                {
                    error = "json element parsed as null";
                    return false;
                }

                itemNode["location"] = parsed;
                return true;
            }
            catch (Exception ex)
            {
                error = $"json element parse failed: {ex.Message}";
                return false;
            }
        }

        try
        {
            var locationJson = jsonUtil.Serialize(location);
            if (string.IsNullOrWhiteSpace(locationJson))
            {
                error = "serialized location json is empty";
                return false;
            }

            var parsed = JsonNode.Parse(locationJson);
            if (parsed is null)
            {
                error = "serialized location json parsed as null";
                return false;
            }

            itemNode["location"] = parsed;
            return true;
        }
        catch (Exception ex)
        {
            error = $"location serialization failed: {ex.Message}";
            return false;
        }
    }

    private static JsonNode? ToJsonNode<TValue>(TValue value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonNode node)
        {
            return node.DeepClone();
        }

        if (value is JsonElement element)
        {
            try
            {
                return JsonNode.Parse(element.GetRawText());
            }
            catch
            {
                return JsonValue.Create(element.ToString());
            }
        }

        try
        {
            return JsonSerializer.SerializeToNode(value);
        }
        catch
        {
            return JsonValue.Create(value.ToString());
        }
    }
}
