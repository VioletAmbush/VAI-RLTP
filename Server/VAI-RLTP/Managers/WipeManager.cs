using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class WipeManager : AbstractModManager
{
    protected override string ConfigName => "WipeConfig";
    private const string DefaultPocketsTemplateId = "627a4e6b255f7527fb05a0f6";
    private const string BasePocketsTemplateId = "557596e64bdc2dc2118b4571";
    private const string EquipmentTemplateId = "55d7217a4bdc2d86028b456d";

    public void OnPlayerDied(MongoId sessionId)
    {
        if (!IsEnabled())
        {
            return;
        }

        ClearStash(sessionId);
    }

    private void ClearStash(MongoId sessionId)
    {
        var profile = ModContext.Current.ProfileHelper.GetPmcProfile(sessionId);
        if (profile is null)
        {
            return;
        }

        ClearProfileStash(profile);

        Constants.GetLogger().Info($"{Constants.ModTitle}: Stash wiped! C:");
    }

    public void ClearProfileStash(PmcData profile)
    {
        var inventory = profile.Inventory;
        if (inventory?.Items is null)
        {
            return;
        }

        var items = inventory.Items;
        var pocketsTemplateId = ResolvePocketsTemplateId(items);
        var equipmentId = EnsureEquipmentRoot(profile, items);

        var securedIds = GetSecuredIds(profile, pocketsTemplateId);
        inventory.Items.RemoveAll(item => !securedIds.Contains(item.Id.ToString()));

        if (string.IsNullOrWhiteSpace(equipmentId))
        {
            equipmentId = EnsureEquipmentRoot(profile, items);
        }

        var pockets = inventory.Items.FirstOrDefault(item => string.Equals(GetItemSlotId(item), "Pockets", StringComparison.OrdinalIgnoreCase));
        if (pockets == null)
        {
            Constants.GetLogger().Warning($"{Constants.ModTitle}: Pockets missing, fixing...");

            var id = GenerateId();
            var fallbackTemplateId = string.IsNullOrWhiteSpace(pocketsTemplateId) ? DefaultPocketsTemplateId : pocketsTemplateId;

            var newPocket = new Item
            {
                Id = id,
                Template = new MongoId(fallbackTemplateId),
                ParentId = equipmentId ?? string.Empty,
                SlotId = "Pockets"
            };

            inventory.Items.Add(newPocket);
        }
    }

    private HashSet<string> GetSecuredIds(PmcData profile, string? pocketsTemplateId)
    {
        var securedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inventory = profile.Inventory;
        if (inventory?.Items is null)
        {
            return securedIds;
        }

        var items = inventory.Items;

        var securedContainer = items.FirstOrDefault(item => string.Equals(GetItemSlotId(item), "SecuredContainer", StringComparison.OrdinalIgnoreCase));
        if (securedContainer != null)
        {
            AddContainerIds(securedContainer.Id.ToString(), securedIds, items);
        }

        var configSecuredItems = GetConfigArray("securedItems");
        if (configSecuredItems != null)
        {
            foreach (var item in items)
            {
                var tpl = item.Template.ToString();
                if (string.IsNullOrWhiteSpace(tpl))
                {
                    continue;
                }

                if (configSecuredItems.Any(node => string.Equals(node?.ToString(), tpl, StringComparison.OrdinalIgnoreCase)))
                {
                    AddContainerIds(item.Id.ToString(), securedIds, items);
                }
            }
        }

        var ignoredItems = GetConfigArray("ignoredItems");
        if (ignoredItems != null)
        {
            foreach (var item in items)
            {
                var tpl = item.Template.ToString();
                if (string.IsNullOrWhiteSpace(tpl))
                {
                    continue;
                }

                if (ignoredItems.Any(node => string.Equals(node?.ToString(), tpl, StringComparison.OrdinalIgnoreCase)))
                {
                    securedIds.Add(item.Id.ToString());
                }
            }
        }

        AddIfExists(securedIds, inventory.Stash.ToString());
        AddIfExists(securedIds, inventory.Equipment?.ToString());
        AddIfExists(securedIds, inventory.QuestRaidItems?.ToString());
        AddIfExists(securedIds, inventory.QuestStashItems?.ToString());
        AddIfExists(securedIds, inventory.SortingTable?.ToString());

        foreach (var entry in inventory.HideoutAreaStashes)
        {
            AddIfExists(securedIds, entry.Value.ToString());
        }

        AddPocketsIds(securedIds, items, pocketsTemplateId);

        var questRaidItemsId = inventory.QuestRaidItems?.ToString();
        var questStashItemsId = inventory.QuestStashItems?.ToString();
        foreach (var item in items)
        {
            var parentId = item.ParentId;
            if (!string.IsNullOrWhiteSpace(parentId) &&
                (string.Equals(parentId, questRaidItemsId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(parentId, questStashItemsId, StringComparison.OrdinalIgnoreCase)))
            {
                securedIds.Add(item.Id.ToString());
            }
        }

        var scabbard = items.FirstOrDefault(item => string.Equals(GetItemSlotId(item), "Scabbard", StringComparison.OrdinalIgnoreCase));
        if (scabbard != null)
        {
            securedIds.Add(scabbard.Id.ToString());
        }

        return securedIds;
    }

    private static void AddContainerIds(string? containerId, HashSet<string> ids, List<Item> items)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            return;
        }

        ids.Add(containerId);

        for (var i = 0; i < 20; i++)
        {
            var count = ids.Count;

            foreach (var item in items)
            {
                var parentId = item.ParentId;
                if (string.IsNullOrWhiteSpace(parentId) || !ids.Contains(parentId))
                {
                    continue;
                }

                ids.Add(item.Id.ToString());
            }

            if (ids.Count == count)
            {
                break;
            }
        }
    }

    private static void AddIfExists(HashSet<string> ids, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ids.Add(value);
        }
    }

    private static string? GetItemSlotId(Item item)
    {
        if (!string.IsNullOrWhiteSpace(item.SlotId))
        {
            return item.SlotId;
        }

        if (item.Location is null)
        {
            return null;
        }

        if (item.Location is JsonObject json)
        {
            return json["slotId"]?.ToString() ?? json["SlotId"]?.ToString();
        }

        if (item.Location is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("slotId", out var slotId))
            {
                return slotId.ToString();
            }

            if (element.TryGetProperty("SlotId", out var slotIdPascal))
            {
                return slotIdPascal.ToString();
            }
        }

        return null;
    }

    private static void AddPocketsIds(HashSet<string> securedIds, List<Item> items, string? pocketsTemplateId)
    {
        foreach (var item in items)
        {
            var slotId = GetItemSlotId(item);
            if (!string.IsNullOrWhiteSpace(slotId) &&
                string.Equals(slotId, "Pockets", StringComparison.OrdinalIgnoreCase))
            {
                securedIds.Add(item.Id.ToString());
                continue;
            }

            if (!string.IsNullOrWhiteSpace(pocketsTemplateId) &&
                string.Equals(item.Template.ToString(), pocketsTemplateId, StringComparison.OrdinalIgnoreCase))
            {
                securedIds.Add(item.Id.ToString());
            }
        }
    }

    private static string? ResolvePocketsTemplateId(List<Item> items)
    {
        var pockets = items.FirstOrDefault(item => string.Equals(GetItemSlotId(item), "Pockets", StringComparison.OrdinalIgnoreCase));
        if (pockets != null)
        {
            var templateId = pockets.Template.ToString();
            return string.IsNullOrWhiteSpace(templateId) ? null : templateId;
        }

        var templates = Constants.GetDatabaseTables()?.Templates?.Items;
        if (templates is null)
        {
            return null;
        }

        foreach (var item in items)
        {
            var templateId = item.Template.ToString();
            if (IsPocketsTemplate(templateId, templates))
            {
                return templateId;
            }
        }

        return null;
    }

    private static bool IsPocketsTemplate(string? templateId, Dictionary<MongoId, TemplateItem> templates)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return false;
        }

        if (string.Equals(templateId, BasePocketsTemplateId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!MongoId.IsValidMongoId(templateId))
        {
            return false;
        }

        if (!templates.TryGetValue(new MongoId(templateId), out var current))
        {
            return false;
        }

        for (var i = 0; i < 12; i++)
        {
            var parentId = GetTemplateParentId(current);
            if (string.IsNullOrWhiteSpace(parentId))
            {
                return false;
            }

            if (string.Equals(parentId, BasePocketsTemplateId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!MongoId.IsValidMongoId(parentId) || !templates.TryGetValue(new MongoId(parentId), out current))
            {
                return false;
            }
        }

        return false;
    }

    private static string? GetTemplateParentId(TemplateItem item)
    {
        var parent = item.Parent.ToString();
        return string.IsNullOrWhiteSpace(parent) ? null : parent;
    }

    private static string? EnsureEquipmentRoot(PmcData profile, List<Item> items)
    {
        var inventory = profile.Inventory;
        if (inventory is null)
        {
            return null;
        }

        var equipmentId = inventory.Equipment?.ToString();
        if (!string.IsNullOrWhiteSpace(equipmentId) &&
            items.Any(item => string.Equals(item.Id.ToString(), equipmentId, StringComparison.OrdinalIgnoreCase)))
        {
            return equipmentId;
        }

        var id = GenerateId();
        var equipmentItem = new Item
        {
            Id = id,
            Template = new MongoId(EquipmentTemplateId)
        };

        items.Add(equipmentItem);
        inventory.Equipment = id;

        return id.ToString();
    }

    private static MongoId GenerateId()
    {
        return Helper.GenerateMongoId();
    }
}
