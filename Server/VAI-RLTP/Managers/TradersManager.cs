using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Path = System.IO.Path;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class TradersManager(
    PresetsManager presetsManager,
    QuestsManager questsManager) : AbstractModManager
{
    private readonly PresetsManager _presetsManager = presetsManager;
    private readonly QuestsManager _questsManager = questsManager;
    private readonly List<JsonObject> _configs = [];
    private ItemConfig? _itemConfig;
    private const int AmmoBatchSize = 30;
    private static readonly HashSet<string> AmmoBatchTraderIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "54cb50c76803fa8b248b4571", // Prapor
        "58330581ace78e27b8b10cee", // Skier
        "5935c25fb3acc3127c3d8cd9", // Peacekeeper
        "5a7c2eca46aef81a7ca2145d", // Mechanic
        "5c0647fdd443bc2504c2d371"  // Jaeger
    };

    protected override string ConfigName => "TradersConfig";

    public override int Priority => 3;

    protected override void AfterPreSpt()
    {
        var traderConfigNames = GetConfigArray("traderConfigNames");
        if (traderConfigNames is null)
        {
            return;
        }

        foreach (var nameNode in traderConfigNames)
        {
            var name = nameNode?.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var path = Path.Combine(ModContext.Current.ConfigPath, "Traders", $"{name}.json");
            if (!File.Exists(path))
            {
                Constants.GetLogger().Warning($"{Constants.ModTitle}: Missing trader config {name}.json");
                continue;
            }

            var json = File.ReadAllText(path);
            if (JsonNode.Parse(json) is not JsonObject config)
            {
                continue;
            }

            if (config["enabled"]?.GetValue<bool>() != true)
            {
                continue;
            }

            _configs.Add(config);
        }
    }

    protected override void PostDbInitialize()
    {
        base.PostDbInitialize();
        _itemConfig = ModContext.Current.ConfigServer.GetConfig<ItemConfig>();
    }

    protected override void AfterPostDb()
    {
        foreach (var entry in DatabaseTables.Traders)
        {
            var traderId = entry.Key.ToString();
            var trader = entry.Value;

            var cfg = _configs.FirstOrDefault(c => string.Equals(c["traderId"]?.ToString(), traderId, StringComparison.OrdinalIgnoreCase));
            if (cfg != null)
            {
                SetTrader(trader, cfg);
            }
            else
            {
                SetTraderDefaults(trader);
            }
        }

        ApplySellPriceOverrides();

        Constants.GetLogger().Info($"{Constants.ModTitle}: Traders changes applied!");
    }

    private void ApplySellPriceOverrides()
    {
        if (_itemConfig is null)
        {
            return;
        }

        var overrides = GetConfigObject("sellPriceOverrides");
        if (overrides is null || overrides.Count == 0)
        {
            return;
        }

        foreach (var (tplId, priceNode) in overrides)
        {
            if (string.IsNullOrWhiteSpace(tplId) || !MongoId.IsValidMongoId(tplId))
            {
                continue;
            }

            var price = GetIntValue(priceNode);
            if (price is null)
            {
                continue;
            }

            _itemConfig.HandbookPriceOverride[new MongoId(tplId)] = new HandbookPriceOverride
            {
                Price = price.Value,
                ParentId = new MongoId("5b5f746686f77447ec5d7708")
            };
        }
    }

    private void SetTraderDefaults(Trader trader)
    {
        if (GetConfigBool("clearAssort"))
        {
            ReplaceAssort(trader);
        }

        if (GetConfigBool("disableSell"))
        {
            ClearItemsBuy(trader);
        }

        if (GetConfigBool("disableInsurance"))
        {
            trader.Base.Insurance.Availability = false;
        }

        if (GetConfigBool("resetTradersTimers"))
        {
            trader.Base.NextResupply = 1631486713;
        }
        Constants.GetLogger().Info($"{Constants.ModTitle}: Trader {trader.Base.Nickname} default changes applied!");
    }

    private void SetTrader(Trader trader, JsonObject config)
    {
        var applyAmmoBatching = ShouldBatchAmmo(trader);

        if (GetBool(config, "clearAssort"))
        {
            ReplaceAssort(trader);
        }

        if (GetBool(config, "disableSell"))
        {
            ClearItemsBuy(trader);
        }

        if (GetBool(config, "disableInsurance"))
        {
            trader.Base.Insurance.Availability = false;
        }

        if (GetBool(config, "lockFromStart"))
        {
            trader.Base.UnlockedByDefault = false;
        }

        if (GetConfigBool("resetTradersTimers"))
        {
            trader.Base.NextResupply = 1631486713;
        }

        if (config["sellableItems"] is JsonArray sellableItems && sellableItems.Count > 0)
        {
            foreach (var node in sellableItems)
            {
                var value = node?.ToString();
                if (string.IsNullOrWhiteSpace(value) || !MongoId.IsValidMongoId(value))
                {
                    continue;
                }

                trader.Base.ItemsBuy.IdList.Add(new MongoId(value));
            }
        }

        if (Constants.AllPresetsUnconditional)
        {
            var presetIds = _presetsManager.ResolveAllPresetIds();
            foreach (var presetId in presetIds)
            {
                SetTraderPreset(trader, config, new JsonObject
                {
                    ["presetId"] = presetId,
                    ["loyaltyLevel"] = 1
                }, applyAmmoBatching);
            }
        }

        if (config["items"] is JsonArray items && items.Count > 0)
        {
            foreach (var itemNode in items.OfType<JsonObject>())
            {
                ProcessTraderItem(trader, config, itemNode, applyAmmoBatching);
            }
        }

        if (config["categorizedItems"] is JsonObject categorized)
        {
            foreach (var (_, categoryNode) in categorized)
            {
                if (categoryNode is not JsonArray categoryItems)
                {
                    continue;
                }

                foreach (var itemNode in categoryItems.OfType<JsonObject>())
                {
                    ProcessTraderItem(trader, config, itemNode, applyAmmoBatching);
                }
            }
        }

        ApplyConfiguredAssortLimits(trader, config, applyAmmoBatching);
        Constants.GetLogger().Info($"{Constants.ModTitle}: Trader {trader.Base.Nickname} changes applied!");
    }

    private void ProcessTraderItem(Trader trader, JsonObject traderConfig, JsonObject item, bool applyAmmoBatching)
    {
        var presetId = item["presetId"]?.ToString();
        var itemTpl = item["itemTemplateId"]?.ToString();

        if (!Constants.AllPresetsUnconditional && !string.IsNullOrWhiteSpace(presetId))
        {
            SetTraderPreset(trader, traderConfig, item, applyAmmoBatching);
        }

        if (!string.IsNullOrWhiteSpace(itemTpl))
        {
            SetTraderItem(trader, traderConfig, item, applyAmmoBatching);
        }
    }

    private void SetTraderPreset(Trader trader, JsonObject traderConfig, JsonObject item, bool applyAmmoBatching)
    {
        var presetId = item["presetId"]?.ToString();
        if (string.IsNullOrWhiteSpace(presetId))
        {
            return;
        }

        var presetData = _presetsManager.ResolvePreset(presetId, "hideout");
        if (presetData is null)
        {
            return;
        }

        var presetItems = ConvertPresetItems(presetData.Items);
        if (presetItems.Count == 0)
        {
            return;
        }

        var rootTemplateId = presetData.Items.FirstOrDefault(entry =>
            string.Equals(entry["_id"]?.ToString(), presetData.RootId, StringComparison.OrdinalIgnoreCase))?["_tpl"]?.ToString();

        trader.Assort.Items.AddRange(presetItems);

        SetLoyaltyLevel(trader, presetData.RootId, item);
        SetTraderItemCount(item, trader, presetData.RootId, rootTemplateId, applyAmmoBatching);
        SetTraderItemPrice(item, trader, traderConfig, presetData.RootId, rootTemplateId);
        SetTraderItemQuest(item, trader, presetData.RootId, presetData);
    }

    private void SetTraderItem(Trader trader, JsonObject traderConfig, JsonObject item, bool applyAmmoBatching)
    {
        var templateId = item["itemTemplateId"]?.ToString();
        if (string.IsNullOrWhiteSpace(templateId) || !MongoId.IsValidMongoId(templateId))
        {
            return;
        }

        var rootId = GenerateId();
        var stackCount = ResolveStackCount(item, templateId, applyAmmoBatching) ?? 1;
        var buyRestriction = ResolveBuyRestriction(item, templateId, stackCount);

        var newItem = new Item
        {
            Id = rootId,
            Template = new MongoId(templateId),
            ParentId = "hideout",
            SlotId = "hideout",
            Upd = new Upd
            {
                StackObjectsCount = stackCount,
                UnlimitedCount = true,
                BuyRestrictionMax = buyRestriction,
                BuyRestrictionCurrent = 0
            }
        };

        trader.Assort.Items.Add(newItem);

        SetLoyaltyLevel(trader, rootId.ToString(), item);
        SetTraderItemPrice(item, trader, traderConfig, rootId.ToString(), templateId);
        SetTraderItemQuest(item, trader, rootId.ToString());
    }

    private void SetLoyaltyLevel(Trader trader, string rootId, JsonObject item)
    {
        var loyaltyLevel = GetIntValue(item["loyaltyLevel"]) ?? 1;
        if (!MongoId.IsValidMongoId(rootId))
        {
            return;
        }

        trader.Assort.LoyalLevelItems[new MongoId(rootId)] = loyaltyLevel;
    }

    private void SetTraderItemCount(JsonObject itemConfig, Trader trader, string rootId, string? templateId, bool applyAmmoBatching)
    {
        if (!MongoId.IsValidMongoId(rootId))
        {
            return;
        }

        var rootItem = trader.Assort.Items.FirstOrDefault(i => string.Equals(i.Id.ToString(), rootId, StringComparison.OrdinalIgnoreCase));
        if (rootItem == null)
        {
            return;
        }

        var stackCount = ResolveStackCount(itemConfig, templateId, applyAmmoBatching);
        if (stackCount is null)
        {
            return;
        }

        var buyRestriction = ResolveBuyRestriction(itemConfig, templateId, stackCount.Value);
        rootItem.Upd ??= new Upd();
        rootItem.Upd.StackObjectsCount = stackCount.Value;
        rootItem.Upd.UnlimitedCount = true;
        rootItem.Upd.BuyRestrictionMax = buyRestriction;
        rootItem.Upd.BuyRestrictionCurrent = 0;
    }

    private void SetTraderItemPrice(JsonObject item, Trader trader, JsonObject traderConfig, string rootId, string? itemTemplateId)
    {
        if (!MongoId.IsValidMongoId(rootId))
        {
            return;
        }

        if (Constants.MinimumPrices)
        {
            trader.Assort.BarterScheme[new MongoId(rootId)] =
            [
                [
                    new BarterScheme
                    {
                        Template = new MongoId("5449016a4bdc2d6f028b456f"),
                        Count = 1
                    }
                ]
            ];
            return;
        }

        var resolvedTemplateId = itemTemplateId ?? item["itemTemplateId"]?.ToString();
        var isAmmo = !string.IsNullOrWhiteSpace(resolvedTemplateId) && Helper.IsAmmo(resolvedTemplateId);
        var stackCount = ResolveStackCount(item, resolvedTemplateId, ShouldBatchAmmo(trader)) ?? 1;

        var priceArray = new List<BarterScheme>();
        if (item["price"] is JsonArray priceConfig)
        {
            foreach (var priceNode in priceConfig.OfType<JsonObject>())
            {
                var priceTpl = priceNode["templateId"]?.ToString();
                if (string.IsNullOrWhiteSpace(priceTpl) || !MongoId.IsValidMongoId(priceTpl))
                {
                    continue;
                }

                var countValue = GetNumberValue(priceNode["count"]) ?? 1;
                var priceEntry = new BarterScheme
                {
                    Template = new MongoId(priceTpl),
                    Count = 1
                };

                var moneyMultiplier = GetNumberValue(traderConfig["moneyPriceMultiplier"]);
                if (moneyMultiplier is not null &&
                    moneyMultiplier.Value != 0 &&
                    Helper.IsMoney(priceTpl) &&
                    !Helper.IsMoney(resolvedTemplateId ?? string.Empty))
                {
                    countValue *= moneyMultiplier.Value;
                }

                if (isAmmo)
                {
                    countValue *= stackCount;
                }

                var count = isAmmo && Helper.IsMoney(priceTpl)
                    ? (int)Math.Round(countValue, MidpointRounding.AwayFromZero)
                    : (int)Math.Ceiling(countValue);
                if (count < 1)
                {
                    count = 1;
                }

                priceEntry.Count = count;

                if (priceTpl == "59f32bb586f774757e1e8442")
                {
                    priceEntry.Side = DogtagExchangeSide.Bear;
                    priceEntry.Level = 1;
                }

                if (priceTpl == "59f32c3b86f77472a31742f0")
                {
                    priceEntry.Side = DogtagExchangeSide.Usec;
                    priceEntry.Level = 1;
                }

                priceArray.Add(priceEntry);
            }
        }

        if (priceArray.Count == 0)
        {
            priceArray.Add(new BarterScheme
            {
                Template = new MongoId("5449016a4bdc2d6f028b456f"),
                Count = 1
            });
        }

        trader.Assort.BarterScheme[new MongoId(rootId)] = new List<List<BarterScheme>> { priceArray };
    }

    private int? ResolveStackCount(JsonObject itemConfig, string? templateId, bool applyAmmoBatching)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return GetItemCount(itemConfig);
        }

        if (applyAmmoBatching && Helper.IsAmmo(templateId))
        {
            return ResolveAmmoStackCount(templateId);
        }

        return GetItemCount(itemConfig) ?? 1;
    }

    private int ResolveBuyRestriction(JsonObject itemConfig, string? templateId, int stackCount)
    {
        var configCount = GetItemCount(itemConfig);
        if (configCount is null)
        {
            return stackCount;
        }

        if (!string.IsNullOrWhiteSpace(templateId) && Helper.IsAmmo(templateId) && stackCount > 0)
        {
            var stacks = (int)Math.Ceiling(configCount.Value / (double)stackCount);
            return Math.Max(stacks, 1);
        }

        return Math.Max(configCount.Value, stackCount);
    }

    private static int ResolveAmmoStackCount(string templateId)
    {
        var maxStack = Helper.GetStackMaxSize(templateId, AmmoBatchSize);
        var count = Math.Min(AmmoBatchSize, maxStack);
        return count < 1 ? 1 : count;
    }

    private void SetTraderItemQuest(JsonObject item, Trader trader, string rootId, PresetsManager.PresetData? presetData = null)
    {
        if (Constants.NoQuestlockedItems)
        {
            return;
        }

        var questId = item["questId"]?.ToString();
        if (string.IsNullOrWhiteSpace(questId))
        {
            return;
        }

        if (Constants.IgnoreMockQuestlocks && questId == "123")
        {
            return;
        }

        var questState = item["questState"]?.ToString() ?? "success";
        questState = questState.ToLowerInvariant();
        if (questState is not ("success" or "started" or "fail"))
        {
            questState = "success";
        }

        if (!MongoId.IsValidMongoId(rootId) || !MongoId.IsValidMongoId(questId))
        {
            return;
        }

        if (!trader.QuestAssort.TryGetValue(questState, out var stateDict))
        {
            stateDict = new Dictionary<MongoId, MongoId>();
            trader.QuestAssort[questState] = stateDict;
        }

        stateDict[new MongoId(rootId)] = new MongoId(questId);

        var loyaltyLevel = GetIntValue(item["loyaltyLevel"]) ?? 1;
        var traderId = trader.Base.Id.ToString();

        if (presetData != null)
        {
            _questsManager.SetQuestUnlockReward(new QuestRewardRequest(
                QuestId: questId,
                LoyaltyLevel: loyaltyLevel,
                TraderId: traderId,
                ItemId: rootId,
                TemplateId: null,
                PresetData: presetData,
                QuestState: questState));
        }
        else
        {
            _questsManager.SetQuestUnlockReward(new QuestRewardRequest(
                QuestId: questId,
                LoyaltyLevel: loyaltyLevel,
                TraderId: traderId,
                ItemId: rootId,
                TemplateId: item["itemTemplateId"]?.ToString(),
                PresetData: null,
                QuestState: questState));
        }
    }

    private static void ReplaceAssort(Trader trader)
    {
        trader.Assort.Items = new List<Item>();
        trader.Assort.BarterScheme = new Dictionary<MongoId, List<List<BarterScheme>>>();
        trader.Assort.LoyalLevelItems = new Dictionary<MongoId, int>();

        var questAssort = trader.QuestAssort;
        if (questAssort is null)
        {
            return;
        }

        questAssort.Clear();
        questAssort["started"] = new Dictionary<MongoId, MongoId>();
        questAssort["success"] = new Dictionary<MongoId, MongoId>();
        questAssort["fail"] = new Dictionary<MongoId, MongoId>();
    }

    private static void ClearItemsBuy(Trader trader)
    {
        trader.Base.ItemsBuy.Category = new HashSet<MongoId>();
        trader.Base.ItemsBuy.IdList = new HashSet<MongoId>();
    }

    private void ApplyConfiguredAssortLimits(Trader trader, JsonObject config, bool applyAmmoBatching)
    {
        var overrides = new Dictionary<string, (int StackCount, int BuyRestriction)>(StringComparer.OrdinalIgnoreCase);

        if (config["items"] is JsonArray items)
        {
            foreach (var itemNode in items.OfType<JsonObject>())
            {
                AddAssortOverride(overrides, itemNode, applyAmmoBatching);
            }
        }

        if (config["categorizedItems"] is JsonObject categorized)
        {
            foreach (var (_, categoryNode) in categorized)
            {
                if (categoryNode is not JsonArray categoryItems)
                {
                    continue;
                }

                foreach (var itemNode in categoryItems.OfType<JsonObject>())
                {
                    AddAssortOverride(overrides, itemNode, applyAmmoBatching);
                }
            }
        }

        if (overrides.Count == 0)
        {
            return;
        }

        var assortItems = trader.Assort?.Items;
        if (assortItems is null || assortItems.Count == 0)
        {
            return;
        }

        foreach (var item in assortItems)
        {
            if (!string.Equals(item.ParentId, "hideout", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var templateId = item.Template.ToString();
            if (string.IsNullOrWhiteSpace(templateId) || !overrides.TryGetValue(templateId, out var data))
            {
                continue;
            }

            item.Upd ??= new Upd();
            item.Upd.StackObjectsCount = data.StackCount;
            item.Upd.UnlimitedCount = true;
            item.Upd.BuyRestrictionMax = data.BuyRestriction;
            item.Upd.BuyRestrictionCurrent = 0;
        }

        var overrideMap = ModContext.Current.AssortOverrides;
        var traderKeyPrefix = $"{trader.Base.Id}:";
        var staleKeys = overrideMap.Keys.Where(key => key.StartsWith(traderKeyPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in staleKeys)
        {
            overrideMap.Remove(key);
        }

        foreach (var (templateId, data) in overrides)
        {
            overrideMap[$"{trader.Base.Id}:{templateId}"] = data;
        }
    }


    private void AddAssortOverride(Dictionary<string, (int StackCount, int BuyRestriction)> overrides, JsonObject item, bool applyAmmoBatching)
    {
        var templateId = item["itemTemplateId"]?.ToString();
        if (string.IsNullOrWhiteSpace(templateId) || !MongoId.IsValidMongoId(templateId))
        {
            return;
        }

        if (!Helper.IsAmmo(templateId) && !Helper.IsMedical(templateId))
        {
            return;
        }

        var stackCount = ResolveStackCount(item, templateId, applyAmmoBatching) ?? 1;
        var buyRestriction = ResolveBuyRestriction(item, templateId, stackCount);
        overrides[templateId] = (stackCount, buyRestriction);
    }

    private static bool ShouldBatchAmmo(Trader trader)
    {
        var id = trader.Base.Id.ToString();
        return !string.IsNullOrWhiteSpace(id) && AmmoBatchTraderIds.Contains(id);
    }

    private static bool GetBool(JsonObject obj, string key)
    {
        return obj[key] is JsonValue value && value.TryGetValue<bool>(out var result) && result;
    }

    private static int? GetItemCount(JsonObject obj)
    {
        return GetIntValue(obj["count"]);
    }

    private static int? GetIntValue(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var intVal))
            {
                return intVal;
            }

            if (value.TryGetValue<long>(out var longVal))
            {
                return (int)longVal;
            }

            if (value.TryGetValue<double>(out var doubleVal))
            {
                return (int)doubleVal;
            }
        }

        return null;
    }

    private static double? GetNumberValue(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out var doubleVal))
            {
                return doubleVal;
            }

            if (value.TryGetValue<int>(out var intVal))
            {
                return intVal;
            }
        }

        return null;
    }

    private static List<Item> ConvertPresetItems(IEnumerable<JsonObject> items)
    {
        var result = new List<Item>();
        var jsonUtil = Constants.GetJsonUtil();

        foreach (var node in items)
        {
            try
            {
                var item = jsonUtil.Deserialize<Item>(node.ToJsonString());
                if (item != null)
                {
                    result.Add(item);
                }
            }
            catch
            {
                // ignore bad item
            }
        }

        return result;
    }

    private static MongoId GenerateId()
    {
        return Helper.GenerateMongoId();
    }
}
