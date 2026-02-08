using System.Linq;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Servers;
using VAI.RLTP.Routers;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class DeathManager(
    WipeManager wipeManager,
    HealingManager healingManager,
    StartManager startManager,
    SaveServer saveServer) : AbstractModManager
{
    private readonly WipeManager _wipeManager = wipeManager;
    private readonly HealingManager _healingManager = healingManager;
    private readonly StartManager _startManager = startManager;
    private readonly SaveServer _saveServer = saveServer;

    protected override string ConfigName => "DeathConfig";

    public async Task HandleRaidEnd(RaidEndRequestData info, MongoId sessionId)
    {
        if (!IsEnabled())
        {
            return;
        }

        var results = GetResultsNode(info);
        if (results is null || !IsVaiProfile(results))
        {
            return;
        }

        var result = results["result"]?.ToString();
        if (!string.Equals(result, "SURVIVED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(result, "RUNNER", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(result, "TRANSIT", StringComparison.OrdinalIgnoreCase))
        {
            _wipeManager.OnPlayerDied(sessionId);
            _healingManager.OnPlayerDied(sessionId);
        }

        _startManager.OnPlayerExtracted(results, sessionId);

        await _saveServer.SaveProfileAsync(sessionId);
    }

    public void HandleRagfairFind(MongoId sessionId)
    {
        if (!IsEnabled() || !Constants.PrintPresetsOnFleaEnter)
        {
            return;
        }

        var profile = ModContext.Current.ProfileHelper.GetPmcProfile(sessionId);
        if (profile is null)
        {
            return;
        }

        PrintSlot(profile, "FirstPrimaryWeapon");
        PrintSlot(profile, "SecondPrimaryWeapon");
        PrintSlot(profile, "Holster");

        PrintChildren(profile, "5b6d9ce188a4501afc1b2b25");
        PrintChildren(profile, "5c0a840b86f7742ffa4f2482");
    }

    private static JsonObject? GetResultsNode(RaidEndRequestData info)
    {
        if (info.ExtensionData.TryGetValue("results", out var results) || info.ExtensionData.TryGetValue("Results", out results))
        {
            try
            {
                return JsonNode.Parse(results.GetRawText()) as JsonObject;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static bool IsVaiProfile(JsonObject results)
    {
        var profile = results["profile"] as JsonObject ?? results["Profile"] as JsonObject;
        var info = profile?["Info"] as JsonObject ?? profile?["info"] as JsonObject;
        var version = info?["GameVersion"]?.ToString() ?? info?["gameVersion"]?.ToString();
        return !string.IsNullOrWhiteSpace(version) && version.StartsWith("VAI Rogue-lite", StringComparison.OrdinalIgnoreCase);
    }

    private void PrintSlot(PmcData profile, string slot)
    {
        var inventory = profile.Inventory;
        if (inventory?.Items is null)
        {
            return;
        }

        var items = inventory.Items;
        var root = items.FirstOrDefault(item => string.Equals(item.SlotId, slot, StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            return;
        }

        var tree = Helper.GetItemTree(items, root);
        var json = SerializePresetTree(tree);
        Console.WriteLine($"\"\": {json},");
    }

    private void PrintChildren(PmcData profile, string parentTemplateId)
    {
        var inventory = profile.Inventory;
        if (inventory?.Items is null)
        {
            return;
        }

        var items = inventory.Items;
        var containers = items
            .Where(item => string.Equals(item.Template.ToString(), parentTemplateId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var i = 0;
        foreach (var container in containers)
        {
            var containerId = container.Id.ToString();
            if (string.IsNullOrWhiteSpace(containerId))
            {
                continue;
            }

            var roots = items.Where(item => string.Equals(item.ParentId, containerId, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var root in roots)
            {
                var tree = Helper.GetItemTree(items, root);
                var json = SerializePresetTree(tree);
                Console.WriteLine($"\"Container_{i}\": {json},");
            }

            i++;
        }
    }

    private string SerializePresetTree(IReadOnlyList<Item> tree)
    {
        if (tree.Count == 0)
        {
            return "[]";
        }

        var json = JsonUtil.Serialize(tree, false);
        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonArray array && array.Count > 0)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is not JsonObject item)
                    {
                        continue;
                    }

                    var isRoot = i == 0;
                    if (isRoot)
                    {
                        RemoveCaseInsensitive(item, "location");
                    }

                    if (TryGetObjectCaseInsensitive(item, "upd", out var upd, out var updKey))
                    {
                        RemoveCaseInsensitive(upd, "Repairable");
                        RemoveCaseInsensitive(upd, "SpawnedInSession");

                        if (isRoot)
                        {
                            RemoveCaseInsensitive(upd, "sptPresetId");
                        }

                        if (upd.Count == 0 && !string.IsNullOrWhiteSpace(updKey))
                        {
                            item.Remove(updKey);
                        }
                    }
                }
            }

            return node?.ToJsonString() ?? json;
        }
        catch
        {
            return json;
        }
    }

    private static void RemoveCaseInsensitive(JsonObject obj, string key)
    {
        var match = obj.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)).Key;
        if (!string.IsNullOrWhiteSpace(match))
        {
            obj.Remove(match);
        }
    }

    private static bool TryGetObjectCaseInsensitive(JsonObject obj, string key, out JsonObject value, out string? actualKey)
    {
        foreach (var entry in obj)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                actualKey = entry.Key;
                value = entry.Value as JsonObject ?? new JsonObject();
                return entry.Value is JsonObject;
            }
        }

        actualKey = null;
        value = new JsonObject();
        return false;
    }
}
