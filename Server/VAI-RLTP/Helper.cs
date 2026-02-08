using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Server;

namespace VAI.RLTP;

public static class Helper
{
    private const string AmmoParentId = "5485a8684bdc2da71d8b4567";
    private const string AmmoBoxParentId = "543be5cb4bdc2deb348b4568";
    private const string MedicalParentId = "543be5664bdc2dd4348b4569";

    public static void SetItemBuffs(DatabaseTables databaseTables, TemplateItem item, JsonNode? buffsConfig)
    {
        if (buffsConfig is null)
        {
            throw new ArgumentNullException(nameof(buffsConfig));
        }

        var itemId = item.Id.ToString();
        if (string.IsNullOrWhiteSpace(itemId))
        {
            throw new InvalidOperationException("Item _id missing while setting stimulator buffs.");
        }

        var buffName = $"Buff{itemId}";
        var buffs = ConfigMapper.MapBuffs(buffsConfig);

        var buffsTable = databaseTables.Globals.Configuration.Health.Effects.Stimulator.Buffs;
        if (buffsTable is null)
        {
            throw new InvalidOperationException("Stimulator buffs table not found while setting item buffs.");
        }

        buffsTable[buffName] = buffs;

        var props = item.Properties;
        if (props is null)
        {
            throw new InvalidOperationException("Item properties missing while setting stimulator buffs.");
        }

        props.StimulatorBuffs = buffName;
    }

    public static void TrySetItemRarity(TemplateItem item, JsonObject? itemConfig)
    {
        var rarityNode = itemConfig?["rarity"];
        string? rarity = null;
        if (rarityNode is JsonValue value)
        {
            if (value.TryGetValue<string>(out var stringValue))
            {
                rarity = stringValue;
            }
            else if (value.TryGetValue<int>(out var intValue))
            {
                rarity = intValue.ToString();
            }
            else if (value.TryGetValue<double>(out var doubleValue))
            {
                rarity = ((int)doubleValue).ToString();
            }
        }

        if (string.IsNullOrWhiteSpace(rarity))
        {
            return;
        }

        if (item.Properties is not null)
        {
            item.Properties.BackgroundColor = ConfigMapper.MapRarity(rarity);
        }
    }

    public static void IterateConfigItems(
        DatabaseTables databaseTables,
        JsonNode? config,
        Action<TemplateItem, JsonObject, DatabaseTables, JsonNode?>? action = null,
        bool trySetRarity = true)
    {
        if (config is not JsonObject obj)
        {
            return;
        }

        if (obj["items"] is not JsonObject itemsConfig)
        {
            return;
        }

        var items = databaseTables.Templates.Items;

        foreach (var (_, node) in itemsConfig)
        {
            if (node is not JsonObject itemConfig)
            {
                continue;
            }

            var id = itemConfig["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!MongoId.IsValidMongoId(id))
            {
                continue;
            }

            if (!items.TryGetValue(new MongoId(id), out var item))
            {
                Constants.GetLogger().Error($"{Constants.ModTitle}: Item with id {id} not found!");
                continue;
            }

            if (trySetRarity)
            {
                TrySetItemRarity(item, itemConfig);
            }

            action?.Invoke(item, itemConfig, databaseTables, config);
        }
    }

    public static List<Item> GetItemTree(IEnumerable<Item> inventory, Item root)
    {
        var result = new List<Item> { root };
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.Id.ToString() };

        while (true)
        {
            var count = ids.Count;

            foreach (var item in inventory)
            {
                var itemId = item.Id.ToString();
                if (ids.Contains(itemId))
                {
                    continue;
                }

                var parentId = item.ParentId;
                if (!string.IsNullOrWhiteSpace(parentId) && ids.Contains(parentId))
                {
                    result.Add(item);
                    ids.Add(itemId);
                }
            }

            if (count == ids.Count)
            {
                break;
            }
        }

        return result;
    }

    public static bool IsStackable(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return false;
        }

        var tables = Constants.GetDatabaseTables();
        if (!MongoId.IsValidMongoId(templateId))
        {
            return false;
        }

        if (!tables.Templates.Items.TryGetValue(new MongoId(templateId), out var item))
        {
            return false;
        }

        return item.Properties?.StackMaxSize > 1;
    }

    public static bool IsMoney(string templateId)
    {
        return templateId is "5449016a4bdc2d6f028b456f" or "5696686a4bdc2da3298b456a" or "569668774bdc2da2298b4568";
    }

    public static bool IsAmmo(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId) || !MongoId.IsValidMongoId(templateId))
        {
            return false;
        }

        var tables = Constants.GetDatabaseTables();
        if (!tables.Templates.Items.TryGetValue(new MongoId(templateId), out var item))
        {
            return false;
        }

        if (HasParent(tables, item, AmmoBoxParentId))
        {
            return false;
        }

        return HasParent(tables, item, AmmoParentId);
    }

    public static bool IsAmmoBox(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId) || !MongoId.IsValidMongoId(templateId))
        {
            return false;
        }

        var tables = Constants.GetDatabaseTables();
        if (!tables.Templates.Items.TryGetValue(new MongoId(templateId), out var item))
        {
            return false;
        }

        return HasParent(tables, item, AmmoBoxParentId);
    }

    public static bool IsMedical(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId) || !MongoId.IsValidMongoId(templateId))
        {
            return false;
        }

        var tables = Constants.GetDatabaseTables();
        if (!tables.Templates.Items.TryGetValue(new MongoId(templateId), out var item))
        {
            return false;
        }

        return HasParent(tables, item, MedicalParentId);
    }

    private static bool HasParent(DatabaseTables tables, TemplateItem item, string parentId)
    {
        var current = item;
        for (var i = 0; i < 12; i++)
        {
            var currentParent = GetParentId(current);
            if (string.IsNullOrWhiteSpace(currentParent))
            {
                return false;
            }

            if (string.Equals(currentParent, parentId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!MongoId.IsValidMongoId(currentParent) ||
                !tables.Templates.Items.TryGetValue(new MongoId(currentParent), out var parentItem))
            {
                return false;
            }

            current = parentItem;
        }

        return false;
    }

    private static string? GetParentId(TemplateItem item)
    {
        var parent = item.Parent.ToString();
        return string.IsNullOrWhiteSpace(parent) ? null : parent;
    }

    public static int GetStackMaxSize(string templateId, int fallback = 1)
    {
        if (string.IsNullOrWhiteSpace(templateId) || !MongoId.IsValidMongoId(templateId))
        {
            return fallback;
        }

        var tables = Constants.GetDatabaseTables();
        if (!tables.Templates.Items.TryGetValue(new MongoId(templateId), out var item))
        {
            return fallback;
        }

        var max = item.Properties?.StackMaxSize;
        if (max is null || max.Value < 1)
        {
            return fallback;
        }

        return max.Value;
    }

    public static int GetStashSize(PmcData profile)
    {
        var bonuses = profile.Bonuses;
        if (bonuses is null)
        {
            return 70;
        }

        var stashBonusCount = bonuses.Count(b => b.Type == BonusType.StashSize);

        return stashBonusCount switch
        {
            1 => 30,
            2 => 40,
            3 => 50,
            _ => 70
        };
    }

    public static void FillLocations(PmcData profile, DatabaseTables databaseTables)
    {
        var stashSize = GetStashSize(profile);
        var stashMap = new List<bool[]>();

        for (var r = 0; r < stashSize; r++)
        {
            stashMap.Add(new bool[10]);
        }

        var inventory = profile.Inventory;
        if (inventory?.Items is null)
        {
            return;
        }

        var items = inventory.Items;
        var stashId = inventory.Stash?.ToString();
        if (string.IsNullOrWhiteSpace(stashId))
        {
            return;
        }

        var stashItems = items.Where(item => string.Equals(item.ParentId, stashId, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var item in stashItems)
        {
            if (item.Location is not ItemLocation location)
            {
                continue;
            }

            if (!databaseTables.Templates.Items.TryGetValue(item.Template, out var template))
            {
                continue;
            }

            var width = template.Properties?.Width ?? 1;
            var height = template.Properties?.Height ?? 1;

            var x = location.X ?? 0;
            var y = location.Y ?? 0;
            var vertical = location.R == ItemRotation.Vertical;

            IterateRect(x, y, width, height, vertical, stashMap, (Action<int, int>)((ix, iy) => stashMap[iy][ix] = true));
        }

        foreach (var item in stashItems)
        {
            if (item.Location is ItemLocation)
            {
                continue;
            }

            if (!databaseTables.Templates.Items.TryGetValue(item.Template, out var template))
            {
                continue;
            }

            var width = template.Properties?.Width ?? 1;
            var height = template.Properties?.Height ?? 1;

            var location = FindItemLocation(width, height, stashMap);
            if (location is null)
            {
                Constants.GetLogger().Error($"{Constants.ModTitle}: Error - could not place item in stash");
                continue;
            }

            item.Location = location;

            var x = location.X ?? 0;
            var y = location.Y ?? 0;
            var vertical = location.R == ItemRotation.Vertical;
            IterateRect(x, y, width, height, vertical, stashMap, (Action<int, int>)((ix, iy) => stashMap[iy][ix] = true));
        }
    }

    public static Dictionary<MongoId, int?> GetTradersRecords(int val)
    {
        var result = new Dictionary<MongoId, int?>();
        AddTraderRecord(result, AcronymToTraderId("p"), val);
        AddTraderRecord(result, AcronymToTraderId("t"), val);
        AddTraderRecord(result, AcronymToTraderId("f"), val);
        AddTraderRecord(result, AcronymToTraderId("s"), val);
        AddTraderRecord(result, AcronymToTraderId("pk"), val);
        AddTraderRecord(result, AcronymToTraderId("m"), val);
        AddTraderRecord(result, AcronymToTraderId("r"), val);
        AddTraderRecord(result, AcronymToTraderId("j"), val);
        return result;
    }

    public static MongoId ToMongoId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !MongoId.IsValidMongoId(value))
        {
            return MongoId.Empty();
        }

        return new MongoId(value);
    }

    private static void AddTraderRecord(Dictionary<MongoId, int?> result, string? traderId, int? val)
    {
        if (string.IsNullOrWhiteSpace(traderId) || !MongoId.IsValidMongoId(traderId))
        {
            return;
        }

        result[new MongoId(traderId)] = val;
    }

    public static string? AcronymToTraderId(string acr)
    {
        if (string.IsNullOrWhiteSpace(acr))
        {
            return null;
        }

        var key = acr.Trim().ToLowerInvariant();
        return key switch
        {
            "p" => "54cb50c76803fa8b248b4571",
            "t" => "54cb57776803fa99248b456e",
            "f" => "579dc571d53a0658a154fbec",
            "s" => "58330581ace78e27b8b10cee",
            "pk" => "5935c25fb3acc3127c3d8cd9",
            "m" => "5a7c2eca46aef81a7ca2145d",
            "r" => "5ac3b934156ae10c4430e83c",
            "j" => "5c0647fdd443bc2504c2d371",
            "re" => "6617beeaa9cfa777ca915b7c",
            _ => null
        };
    }

    public static string ScavTypeToString(string scavType)
    {
        return scavType switch
        {
            "bossBully" => "Reshala",
            "followerBully" => "Reshala Guard",
            "bossTagilla" => "Tagilla",
            "bossKojaniy" => "Shturman",
            "followerKojaniy" => "Shturman Guard",
            "bossSanitar" => "Sanitar",
            "followerSanitar" => "Sanitar Guard",
            "bossKilla" => "Killa",
            "bossBoar" => "Kaban",
            "bossBoarSniper" => "Kaban Sniper",
            "followerBoar" => "Kaban Guard",
            "exUsec" => "Rogue",
            "bossKnight" => "Knight",
            "followerBirdEye" => "Bird Eye",
            "followerBigPipe" => "Big Pipe",
            "bossGluhar" => "Glukhar",
            "followerGluharAssault" => "Glukhar Assault",
            "followerGluharScout" => "Glukhar Scout",
            "followerGluharSecurity" => "Glukhar Security",
            "followerGluharSnipe" => "Glukhar Sniper",
            "sectantPriest" => "Cultist Priest",
            "sectantWarrior" => "Cultist Warrior",
            "marksman" => "Scav Sniper",
            "pmcBot" => "Raider",
            "pmcUSEC" => "USEC PMC",
            "pmcBEAR" => "BEAR PMC",
            _ => $"UNKNOWN SCAV TYPE ({scavType})"
        };
    }

    public static string GenerateSha256Id(string data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        var hash = Convert.ToHexString(bytes).ToLowerInvariant();
        return hash.Length > 24 ? hash[..24] : hash;
    }

    public static MongoId GenerateMongoId()
    {
        return new MongoId(GenerateSha256Id(Guid.NewGuid().ToString("N")));
    }

    private static ItemLocation? FindItemLocation(int width, int height, List<bool[]> stashMap)
    {
        for (var r = 0; r < stashMap.Count; r++)
        {
            for (var c = 0; c < stashMap[r].Length; c++)
            {
                if (stashMap[r][c] || width > stashMap[r].Length - c)
                {
                    continue;
                }

                var foundPlace = true;
                IterateRect(c, r, width, height, false, stashMap, (x, y) =>
                {
                    if (stashMap[y][x])
                    {
                        foundPlace = false;
                    }
                });

                if (!foundPlace)
                {
                    continue;
                }

                return new ItemLocation
                {
                    X = c,
                    Y = r,
                    R = ItemRotation.Horizontal,
                    IsSearched = true
                };
            }
        }

        return null;
    }

    private static void IterateRect(int x, int y, int width, int height, bool rotated, List<bool[]> stashMap, Action<int, int> callback)
    {
        var spanWidth = rotated ? height : width;
        var spanHeight = rotated ? width : height;

        for (var i = 0; i < spanWidth * spanHeight; i++)
        {
            var ix = x + i % spanWidth;
            var iy = y + i / spanWidth;
            if (iy < 0 || iy >= stashMap.Count || ix < 0 || ix >= stashMap[iy].Length)
            {
                continue;
            }

            callback(ix, iy);
        }
    }
}
