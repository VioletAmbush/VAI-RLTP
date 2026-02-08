using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Cloners;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class ItemsManager(LocaleManager localeManager, ICloner cloner) : AbstractModManager
{
    private readonly LocaleManager _localeManager = localeManager;
    private readonly ICloner _cloner = cloner;

    protected override string ConfigName => "ItemsConfig";

    public override int Priority => 2;

    protected override void AfterPostDb()
    {
        var itemsConfig = GetConfigObject("items");
        if (itemsConfig != null)
        {
            foreach (var (itemKey, itemNode) in itemsConfig)
            {
                if (itemNode is not JsonObject itemConfig)
                {
                    continue;
                }

                AddItem(itemKey, itemConfig);
            }
        }

        if (GetConfigBool("addUBGLCompat"))
        {
            AddUbglCompat();
        }

        if (GetConfigBool("add366TKMBubenCompat"))
        {
            Add366TKMBubenCompat();
        }

        if (GetConfigBool("add6851T5000Compat"))
        {
            Add6851T5000Compat();
        }
    }

    private void AddItem(string itemName, JsonObject itemConfig)
    {
        var changes = itemConfig["changes"] as JsonObject;
        var newId = changes?["_id"]?.ToString();

        if (string.IsNullOrWhiteSpace(newId) || !MongoId.IsValidMongoId(newId))
        {
            Constants.GetLogger().Warning($"{Constants.ModTitle}: Empty id in item {itemName}");
            return;
        }

        var copyTemplateId = itemConfig["copyTemplateId"]?.ToString();
        if (string.IsNullOrWhiteSpace(copyTemplateId) || !MongoId.IsValidMongoId(copyTemplateId))
        {
            return;
        }

        var items = DatabaseTables.Templates.Items;
        if (!items.TryGetValue(new MongoId(copyTemplateId), out var copyTemplate))
        {
            return;
        }

        var cloned = _cloner.Clone(copyTemplate);
        if (cloned is null)
        {
            return;
        }

        var json = JsonUtil.Serialize(cloned, false);
        json = json.Replace(copyTemplateId, newId);
        var newItemNode = JsonNode.Parse(json) as JsonObject;
        if (newItemNode is null)
        {
            return;
        }

        if (changes != null)
        {
            SetObjectProperties(newItemNode, changes);
        }

        TemplateItem? newItem = null;
        try
        {
            newItem = JsonUtil.Deserialize<TemplateItem>(newItemNode.ToJsonString());
        }
        catch
        {
            // ignore, fallback below
        }

        if (newItem is null)
        {
            try
            {
                newItem = JsonSerializer.Deserialize<TemplateItem>(newItemNode.ToJsonString(), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                // ignore
            }
        }

        if (newItem is null)
        {
            Constants.GetLogger().Warning($"{Constants.ModTitle}: Failed to deserialize item {newId}");
            return;
        }

        items[new MongoId(newId)] = newItem;

        if (itemConfig["slotId"] != null)
        {
            AddSlotFilter(newId, itemConfig["slotId"]?.ToString() ?? string.Empty);
        }

        if (itemConfig["copyLocale"]?.GetValue<bool>() == true)
        {
            CopyLocale(copyTemplateId, newId, itemConfig["modTitle"]?.ToString());
        }
    }

    private void AddSlotFilter(string newId, string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId) || !MongoId.IsValidMongoId(newId))
        {
            return;
        }

        if (!DatabaseTables.Templates.Items.TryGetValue(new MongoId("55d7217a4bdc2d86028b456d"), out var weapon))
        {
            return;
        }

        var slots = weapon.Properties?.Slots;
        if (slots is null)
        {
            return;
        }

        foreach (var slot in slots)
        {
            var name = slot?.Name ?? string.Empty;
            if (!name.Contains(slotId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var filters = slot?.Properties?.Filters;
            if (filters is null)
            {
                continue;
            }

            foreach (var filter in filters)
            {
                var filterList = filter?.Filter;
                if (filterList is null)
                {
                    continue;
                }

                if (!filterList.Any(entry => string.Equals(entry, newId, StringComparison.OrdinalIgnoreCase)))
                {
                    filterList.Add(newId);
                }
            }
        }
    }

    private void CopyLocale(string copyTemplateId, string newId, string? modTitle)
    {
        var name = _localeManager.GetENLocale($"{copyTemplateId} Name");
        var description = _localeManager.GetENLocale($"{copyTemplateId} Description");
        var shortName = _localeManager.GetENLocale($"{copyTemplateId} ShortName");

        if (!string.IsNullOrWhiteSpace(modTitle))
        {
            name = $"{name} {modTitle}";
            description = $"{description} {modTitle}";
        }

        _localeManager.SetENLocale($"{newId} Name", name);
        _localeManager.SetENLocale($"{newId} Description", description);
        _localeManager.SetENLocale($"{newId} ShortName", shortName);
    }

    private void SetObjectProperties(JsonObject item, JsonObject config)
    {
        foreach (var (changeKey, changeNode) in config)
        {
            if (changeNode is null)
            {
                continue;
            }

            if (!item.TryGetPropertyValue(changeKey, out var itemNode) || itemNode is null)
            {
                continue;
            }

            if (itemNode is JsonArray itemArray && changeNode is JsonArray configArray && itemArray.Count == configArray.Count && configArray.Count > 0)
            {
                if (itemArray[0] is not JsonArray && itemArray[0] is not JsonObject)
                {
                    item[changeKey] = configArray.DeepClone();
                    continue;
                }

                for (var i = 0; i < configArray.Count; i++)
                {
                    if (itemArray[i] is JsonObject itemObj && configArray[i] is JsonObject configObj)
                    {
                        SetObjectProperties(itemObj, configObj);
                    }
                }

                continue;
            }

            if (itemNode is JsonObject itemObjNode && changeNode is JsonObject configObjNode)
            {
                SetObjectProperties(itemObjNode, configObjNode);
                continue;
            }

            if (changeNode is JsonValue value && value.TryGetValue<string>(out var strVal) && string.IsNullOrEmpty(strVal))
            {
                return;
            }

            item[changeKey] = changeNode.DeepClone();
        }
    }

    private void AddUbglCompat()
    {
        var launcherHosts = new[]
        {
            "59e6152586f77473dc057aa1",
            "67495c74dfe62c2d7400002a",
            "59e6687d86f77411d949b251",
            "67495c74dfe62c2d74000029",
            "5ac66cb05acfc40198510a10",
            "5ac66d2e5acfc43b321d4b53",
            "6499849fc93611967b034949",
            "67495c74dfe62c2d74000045",
            "5bf3e03b0db834001d2c4a9c",
            "67495c74dfe62c2d74000032",
            "5644bd2b4bdc2d3b4c8b4572",
            "5ac4cd105acfc40016339859",
            "5bf3e0490db83400196199af",
            "5ab8e9fcd8ce870019439434",
            "59d6088586f774275f37482f",
            "67474dd2a7f5b436b8000025",
            "67495c74dfe62c2d7400003d",
            "59ff346386f77477562ff5e2",
            "5abcbc27d8ce8700182eceeb",
            "5a0ec13bfcdbcb00165aa685"
        };

        foreach (var id in launcherHosts)
        {
            AddLauncherFilter(id, "67495c74dfe62c2d7400002d");
        }

        var olderLaunchers = new[]
        {
            "55d3632e4bdc2d972f8b4569",
            "63d3ce0446bd475bcb50f55f"
        };

        foreach (var id in olderLaunchers)
        {
            AddLauncherFilter(id, "67495c74dfe62c2d7400002c");
        }
    }

    private void AddLauncherFilter(string hostId, string newId)
    {
        if (!MongoId.IsValidMongoId(hostId) || !MongoId.IsValidMongoId(newId))
        {
            return;
        }

        if (!DatabaseTables.Templates.Items.TryGetValue(new MongoId(hostId), out var host))
        {
            return;
        }

        var slots = host.Properties?.Slots;
        if (slots is null)
        {
            return;
        }

        foreach (var slot in slots)
        {
            var name = slot?.Name;
            if (!string.Equals(name, "mod_launcher", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var filters = slot?.Properties?.Filters;
            if (filters is null)
            {
                continue;
            }

            foreach (var filter in filters)
            {
                var filterList = filter?.Filter;
                if (filterList is null)
                {
                    continue;
                }

                if (!filterList.Any(entry => string.Equals(entry, newId, StringComparison.OrdinalIgnoreCase)))
                {
                    filterList.Add(newId);
                }
            }
        }
    }

    private void Add366TKMBubenCompat()
    {
        if (!DatabaseTables.Templates.Items.TryGetValue(new MongoId("6513f0a194c72326990a3868"), out var weapon))
        {
            return;
        }

        var cartridges = weapon.Properties?.Cartridges;
        if (cartridges is null)
        {
            return;
        }

        foreach (var cartridge in cartridges)
        {
            var name = cartridge?.Name;
            if (!string.Equals(name, "cartridges", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var filters = cartridge?.Properties?.Filters;
            if (filters is null)
            {
                continue;
            }

            foreach (var filter in filters)
            {
                var filterList = filter?.Filter;
                if (filterList is null)
                {
                    continue;
                }

                foreach (var newId in new[]
                         {
                             "59e655cb86f77411dc52a77b",
                             "59e6542b86f77411dc52a77a",
                             "59e6658b86f77411d949b250",
                             "5f0596629e22f464da6bbdd9"
                         })
                {
                    if (!filterList.Any(entry => string.Equals(entry, newId, StringComparison.OrdinalIgnoreCase)))
                    {
                        filterList.Add(newId);
                    }
                }
            }
        }
    }

    private void Add6851T5000Compat()
    {
        if (!DatabaseTables.Templates.Items.TryGetValue(new MongoId("5df25b6c0b92095fd441e4cf"), out var weapon))
        {
            return;
        }

        var cartridges = weapon.Properties?.Cartridges;
        if (cartridges is null)
        {
            return;
        }

        foreach (var cartridge in cartridges)
        {
            var name = cartridge?.Name;
            if (!string.Equals(name, "cartridges", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var filters = cartridge?.Properties?.Filters;
            if (filters is null)
            {
                continue;
            }

            foreach (var filter in filters)
            {
                var filterList = filter?.Filter;
                if (filterList is null)
                {
                    continue;
                }

                foreach (var newId in new[] { "6529302b8c26af6326029fb7", "6529243824cbe3c74a05e5c1" })
                {
                    if (!filterList.Any(entry => string.Equals(entry, newId, StringComparison.OrdinalIgnoreCase)))
                    {
                        filterList.Add(newId);
                    }
                }
            }
        }
    }
}
