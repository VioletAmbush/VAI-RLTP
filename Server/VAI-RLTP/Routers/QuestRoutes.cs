using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Utils;

namespace VAI.RLTP.Routers;

[Injectable]
public sealed class QuestRoutes(JsonUtil jsonUtil, QuestRouteCallbacks callbacks)
    : StaticRouter(jsonUtil,
    [
        new RouteAction<ListQuestsRequestData>(
            "/client/quest/list",
            async (url, info, sessionId, output) => await callbacks.HandleQuestList(url, info, sessionId, output)
        )
    ])
{ }

[Injectable]
public sealed class QuestRouteCallbacks
{
    public ValueTask<string> HandleQuestList(string url, ListQuestsRequestData info, MongoId sessionId, string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new ValueTask<string>(output);
        }

        JsonNode? outputNode;
        try
        {
            outputNode = JsonNode.Parse(output);
        }
        catch
        {
            return new ValueTask<string>(output);
        }

        if (outputNode is null)
        {
            return new ValueTask<string>(output);
        }

        var questArray = GetQuestArray(outputNode);
        if (questArray is null)
        {
            if (outputNode is JsonObject rootObj)
            {
                var keys = string.Join(", ", rootObj.Select(entry => entry.Key));
                Constants.GetLogger().Warning($"{Constants.ModTitle}: Quest list hook could not locate quest array. Keys: {keys}");
            }
            else
            {
                Constants.GetLogger().Warning($"{Constants.ModTitle}: Quest list hook could not locate quest array (non-object root).");
            }

            return new ValueTask<string>(output);
        }

        if (questArray.Count == 0)
        {
            return new ValueTask<string>(output);
        }

        return new ValueTask<string>(output);
    }

    private static JsonArray? GetQuestArray(JsonNode node)
    {
        if (node is JsonArray array)
        {
            return array;
        }

        if (node is not JsonObject obj)
        {
            return null;
        }

        if (TryGetArray(obj, "data", out var dataArray) || TryGetArray(obj, "Data", out dataArray))
        {
            return dataArray;
        }

        if (TryGetObject(obj, "data", out var dataObj) || TryGetObject(obj, "Data", out dataObj))
        {
            if (TryGetArray(dataObj, "quests", out dataArray) || TryGetArray(dataObj, "Quests", out dataArray))
            {
                return dataArray;
            }

            if (TryGetArray(dataObj, "data", out dataArray) || TryGetArray(dataObj, "Data", out dataArray))
            {
                return dataArray;
            }
        }

        if (TryGetArray(obj, "quests", out dataArray) || TryGetArray(obj, "Quests", out dataArray))
        {
            return dataArray;
        }

        return null;
    }

    private static bool TryGetArray(JsonObject obj, string key, out JsonArray array)
    {
        array = null!;
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonArray value)
        {
            return false;
        }

        array = value;
        return true;
    }

    private static bool TryGetObject(JsonObject obj, string key, out JsonObject value)
    {
        value = null!;
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonObject result)
        {
            return false;
        }

        value = result;
        return true;
    }

}
