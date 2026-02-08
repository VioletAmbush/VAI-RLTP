using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Spt.Server;
using SPTarkov.Server.Core.Utils;
using VAI.RLTP;

namespace VAI.RLTP.Routers;

[Injectable]
public sealed class ItemEventRoutes(JsonUtil jsonUtil, ItemEventRouteCallbacks callbacks)
    : StaticRouter(jsonUtil,
    [
        new RouteAction<ItemEventRouterRequest>(
            "/client/game/profile/items/moving",
            async (url, info, sessionId, output) => await callbacks.HandleItemEvent(url, info, sessionId, output)
        )
    ])
{ }

[Injectable]
public sealed class ItemEventRouteCallbacks(JsonUtil jsonUtil)
{
    private const int AmmoBatchSize = 30;
    private static readonly HashSet<string> AmmoBatchTraderIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "54cb50c76803fa8b248b4571", // Prapor
        "58330581ace78e27b8b10cee", // Skier
        "5935c25fb3acc3127c3d8cd9", // Peacekeeper
        "5a7c2eca46aef81a7ca2145d", // Mechanic
        "5c0647fdd443bc2504c2d371"  // Jaeger
    };
    private readonly JsonUtil _jsonUtil = jsonUtil;

    public ValueTask<string> HandleItemEvent(string url, ItemEventRouterRequest info, MongoId sessionId, string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new ValueTask<string>(output);
        }

        var databaseTables = Constants.GetDatabaseTables();
        var requestActions = ParseRequestActions(info);
        if (Constants.PrintAmmoBatchDebug)
        {
            var requestJson = _jsonUtil.Serialize(requestActions) ?? "[]";
            Constants.GetLogger().Info($"{Constants.ModTitle}: Ammo batch request parsed actions={requestActions.Count} jsonBuy={requestJson.Contains("buy_from_trader", StringComparison.OrdinalIgnoreCase)}");
            Constants.GetLogger().Info($"{Constants.ModTitle}: Ammo batch request json={requestJson}");
        }

        if (!IsTradingConfirmFromTrader(requestActions))
        {
            if (Constants.PrintAmmoBatchDebug)
            {
                Constants.GetLogger().Info($"{Constants.ModTitle}: Ammo batch skipped - request not identified as trader buy.");
            }
            return new ValueTask<string>(output);
        }

        var allowAmmoBatching = IsAmmoBatchAllowed(requestActions, databaseTables, out var ammoBatchDebug);
        if (Constants.PrintAmmoBatchDebug && !string.IsNullOrWhiteSpace(ammoBatchDebug))
        {
            Constants.GetLogger().Info($"{Constants.ModTitle}: Ammo batch {(allowAmmoBatching ? "enabled" : "disabled")} {ammoBatchDebug}");
        }

        if (!allowAmmoBatching)
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

        var profile = ModContext.Current.ProfileHelper.GetPmcProfile(sessionId);
        var inventoryItems = profile?.Inventory?.Items;
        var stashId = profile?.Inventory?.Stash?.ToString();
        var changed = false;
        var profileItemMissCount = 0;
        var entries = EnumerateNewItemsWithParent(outputNode).ToList();
        var tradeCounts = BuildTradeCounts(requestActions, databaseTables);
        var outputAmmoCounts = BuildOutputAmmoCounts(entries);
        if (Constants.PrintAmmoBatchDebug)
        {
            Constants.GetLogger().Info($"{Constants.ModTitle}: Ammo batch output entries={entries.Count} ammoTypes={outputAmmoCounts.Count} tradeTypes={tradeCounts.Count}");
        }
        foreach (var (itemNode, parentArray) in entries)
        {
            var tpl = GetItemTemplate(itemNode);
            if (string.IsNullOrWhiteSpace(tpl) || !Helper.IsAmmo(tpl) || Helper.IsAmmoBox(tpl))
            {
                continue;
            }

            var stackCount = ResolveAmmoStackCount(tpl);
            var currentCount = GetStackCount(itemNode);
            if (currentCount <= 0)
            {
                currentCount = 1;
            }

            var tradeCount = 0;
            if (tradeCounts.TryGetValue(tpl, out var tradeData) &&
                outputAmmoCounts.TryGetValue(tpl, out var outputCount) &&
                outputCount <= 1)
            {
                tradeCount = tradeData.GetEffectiveCount();
            }

            var desiredCount = tradeCount > 0
                ? Math.Max(currentCount, tradeCount * stackCount)
                : currentCount < stackCount
                    ? currentCount * stackCount
                    : currentCount;

            var needsSplit = desiredCount > stackCount;
            if (desiredCount == currentCount && !needsSplit)
            {
                continue;
            }

            var itemId = itemNode["_id"]?.ToString();
            if (string.IsNullOrWhiteSpace(itemId))
            {
                continue;
            }

            var profileItem = inventoryItems?.FirstOrDefault(item =>
                string.Equals(item.Id.ToString(), itemId, StringComparison.OrdinalIgnoreCase));

            if (profileItem is null)
            {
                profileItemMissCount++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(stashId) ||
                !string.Equals(profileItem.ParentId, stashId, StringComparison.OrdinalIgnoreCase))
            {
                var capacity = ResolveContainerCapacity(profileItem.ParentId, inventoryItems, databaseTables);
                if (capacity > 0 && currentCount > capacity)
                {
                    SetStackCount(itemNode, capacity);
                    profileItem.Upd ??= new Upd();
                    profileItem.Upd.StackObjectsCount = capacity;
                    changed = true;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(stashId) ||
                !string.Equals(profileItem.ParentId, stashId, StringComparison.OrdinalIgnoreCase) ||
                desiredCount <= stackCount)
            {
                SetStackCount(itemNode, desiredCount);
                profileItem.Upd ??= new Upd();
                profileItem.Upd.StackObjectsCount = desiredCount;
                changed = true;
                continue;
            }

            var stackCounts = BuildStackCounts(desiredCount, stackCount);
            if (stackCounts.Count <= 1)
            {
                SetStackCount(itemNode, desiredCount);
                profileItem.Upd ??= new Upd();
                profileItem.Upd.StackObjectsCount = desiredCount;
                changed = true;
                continue;
            }

            SetStackCount(itemNode, stackCounts[0]);
            profileItem.Upd ??= new Upd();
            profileItem.Upd.StackObjectsCount = stackCounts[0];

            var newProfileItems = new List<Item>();
            var newOutputItems = new List<JsonObject>();

            for (var i = 1; i < stackCounts.Count; i++)
            {
                var newId = GenerateId().ToString();
                var newProfileItem = new Item
                {
                    Id = new MongoId(newId),
                    Template = profileItem.Template,
                    ParentId = profileItem.ParentId,
                    SlotId = profileItem.SlotId,
                    Location = null,
                    Upd = new Upd
                    {
                        StackObjectsCount = stackCounts[i]
                    }
                };

                var newOutputItem = CloneItemNode(itemNode, newId, stackCounts[i]);

                newProfileItems.Add(newProfileItem);
                newOutputItems.Add(newOutputItem);
            }

            inventoryItems.AddRange(newProfileItems);
            foreach (var newOutputItem in newOutputItems)
            {
                parentArray.Add(newOutputItem);
            }

            Helper.FillLocations(profile, databaseTables);

            var missingLocation = false;
            foreach (var newProfileItem in newProfileItems)
            {
                if (newProfileItem.Location is null)
                {
                    missingLocation = true;
                    break;
                }
            }

            if (missingLocation)
            {
                foreach (var newProfileItem in newProfileItems)
                {
                    inventoryItems.Remove(newProfileItem);
                }

                foreach (var newOutputItem in newOutputItems)
                {
                    parentArray.Remove(newOutputItem);
                }

                SetStackCount(itemNode, desiredCount);
                profileItem.Upd ??= new Upd();
                profileItem.Upd.StackObjectsCount = desiredCount;

                Constants.GetLogger().Warning($"{Constants.ModTitle}: Not enough stash space to split ammo stack; leaving combined stack.");
                changed = true;
                continue;
            }

            foreach (var newOutputItem in newOutputItems)
            {
                var newId = newOutputItem["_id"]?.ToString();
                if (string.IsNullOrWhiteSpace(newId))
                {
                    continue;
                }

                var placedProfileItem = inventoryItems.FirstOrDefault(item =>
                    string.Equals(item.Id.ToString(), newId, StringComparison.OrdinalIgnoreCase));
                if (placedProfileItem is null || placedProfileItem.Location is null)
                {
                    continue;
                }

                if (!ItemEventPayloadAdapter.TryApplyLocation(newOutputItem, placedProfileItem.Location, _jsonUtil, out var locationError))
                {
                    Constants.GetLogger().Warning(
                        $"{Constants.ModTitle}: Failed to apply location to split ammo item {newId}: {locationError}");
                }
            }

            changed = true;
        }

        if (!changed)
        {
            if (Constants.PrintAmmoBatchDebug)
            {
                Constants.GetLogger().Info($"{Constants.ModTitle}: Ammo batch no changes applied. profileItemMissCount={profileItemMissCount}");
            }
            return new ValueTask<string>(output);
        }

        if (Constants.PrintAmmoBatchDebug)
        {
            Constants.GetLogger().Info($"{Constants.ModTitle}: Ammo batch applied. profileItemMissCount={profileItemMissCount}");
        }

        return new ValueTask<string>(_jsonUtil.Serialize(outputNode));
    }

    private static List<JsonObject> ParseRequestActions(ItemEventRouterRequest request)
    {
        var actions = new List<JsonObject>();

        if (request.Data is not null)
        {
            foreach (var action in request.Data)
            {
                if (action is null)
                {
                    continue;
                }

                actions.Add(BuildActionNode(action));
            }
        }

        return actions;
    }

    private static JsonObject BuildActionNode(BaseInteractionRequestData action)
    {
        var result = new JsonObject();

        if (!string.IsNullOrWhiteSpace(action.Action))
        {
            result["Action"] = action.Action;
            result["action"] = action.Action;
        }

        var fromOwner = BuildOwnerNode(action.FromOwner);
        if (fromOwner is not null)
        {
            result["FromOwner"] = fromOwner;
            result["fromOwner"] = fromOwner.DeepClone();
        }

        var toOwner = BuildOwnerNode(action.ToOwner);
        if (toOwner is not null)
        {
            result["ToOwner"] = toOwner;
            result["toOwner"] = toOwner.DeepClone();
        }

        if (action is ProcessBaseTradeRequestData trade && !string.IsNullOrWhiteSpace(trade.Type))
        {
            result["Type"] = trade.Type;
            result["type"] = trade.Type;
        }

        if (action is ProcessBuyTradeRequestData buy)
        {
            var itemId = buy.ItemId.ToString();
            if (!string.IsNullOrWhiteSpace(itemId))
            {
                result["ItemId"] = itemId;
                result["item_id"] = itemId;
            }

            if (buy.Count is not null)
            {
                result["Count"] = buy.Count.Value;
                result["count"] = buy.Count.Value;
            }
        }

        ItemEventPayloadAdapter.AppendExtensionData(result, action.ExtensionData);
        if (TryResolveTraderIdFromOwners(result, out var traderId))
        {
            result["Tid"] = traderId;
            result["tid"] = traderId;
        }

        return result;
    }

    private static JsonObject? BuildOwnerNode(OwnerInfo? owner)
    {
        if (owner is null)
        {
            return null;
        }

        var result = new JsonObject();
        if (!string.IsNullOrWhiteSpace(owner.Id))
        {
            result["Id"] = owner.Id;
            result["id"] = owner.Id;
        }

        if (!string.IsNullOrWhiteSpace(owner.Type))
        {
            result["Type"] = owner.Type;
            result["type"] = owner.Type;
        }

        ItemEventPayloadAdapter.AppendExtensionData(result, owner.ExtensionData);
        return result;
    }

    private static bool IsTradingConfirmFromTrader(IReadOnlyList<JsonObject> actions)
    {
        foreach (var action in actions)
        {
            var actionName = GetActionName(action);
            var tradeType = GetStringMember(action, "type", "Type");
            if (!IsBuyFromTraderAction(actionName, tradeType))
            {
                continue;
            }

            var traderId = GetStringMember(action, "tid", "Tid", "traderId", "TraderId");
            if (string.IsNullOrWhiteSpace(traderId) && TryResolveTraderIdFromOwners(action, out var ownerTraderId))
            {
                traderId = ownerTraderId;
            }

            if (string.Equals(traderId, "ragfair", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private static bool IsAmmoBatchAllowed(IReadOnlyList<JsonObject> actions, DatabaseTables databaseTables, out string? debugInfo)
    {
        var hasTrade = false;
        var allowed = false;
        var blocked = false;
        var missingTid = 0;
        var actionDetails = new List<string>();

        foreach (var action in actions)
        {
            var actionName = GetActionName(action);
            var tradeType = GetStringMember(action, "type", "Type");
            if (!IsBuyFromTraderAction(actionName, tradeType))
            {
                continue;
            }

            var traderId = GetStringMember(action, "tid", "Tid", "traderId", "TraderId");
            var itemId = GetStringMember(action, "item_id", "itemId", "itemID", "ItemId", "ItemID");
            var count = GetIntMember(action, "count", "Count") ?? 0;
            var tpl = GetStringMember(action, "item_template_id", "itemTemplateId", "itemTemplateID", "tpl", "Tpl", "templateId", "TemplateId");

            hasTrade = true;
            var resolvedTid = string.Empty;
            if (string.IsNullOrWhiteSpace(traderId) && TryResolveTraderIdFromOwners(action, out var ownerTraderId))
            {
                resolvedTid = ownerTraderId;
                traderId = ownerTraderId;
            }

            if (string.IsNullOrWhiteSpace(traderId) && !string.IsNullOrWhiteSpace(itemId) && MongoId.IsValidMongoId(itemId))
            {
                var tables = Constants.GetDatabaseTables();
                if (TryResolveAssortItemById(tables, itemId, out var resolvedTrader, out _))
                {
                    resolvedTid = resolvedTrader.Base.Id.ToString();
                    traderId = resolvedTid;
                }
            }

            actionDetails.Add(
                $"action={actionName} type={tradeType ?? ""} tid={traderId ?? "<missing>"}" +
                $"{(string.IsNullOrWhiteSpace(resolvedTid) ? "" : $"(resolved={resolvedTid})")} " +
                $"item={itemId ?? ""} tpl={tpl ?? ""} count={count}");

            if (string.IsNullOrWhiteSpace(traderId))
            {
                missingTid++;
                continue;
            }

            if (string.Equals(traderId, "ragfair", StringComparison.OrdinalIgnoreCase))
            {
                blocked = true;
                break;
            }

            if (!AmmoBatchTraderIds.Contains(traderId))
            {
                blocked = true;
                break;
            }

            allowed = true;
        }

        if (hasTrade && allowed && !blocked)
        {
            debugInfo = $"hasTrade={hasTrade} allowed={allowed} blocked={blocked} missingTid={missingTid} actions=[{string.Join("; ", actionDetails)}]";
            return true;
        }

        if (blocked)
        {
            debugInfo = $"hasTrade={hasTrade} allowed={allowed} blocked={blocked} missingTid={missingTid} actions=[{string.Join("; ", actionDetails)}]";
            return false;
        }

        debugInfo = $"hasTrade={hasTrade} allowed={allowed} blocked={blocked} missingTid={missingTid} actions=[{string.Join("; ", actionDetails)}]";
        return false;
    }

    private static bool IsBuyFromTraderAction(string? actionName, string? tradeType)
    {
        if (string.Equals(actionName, "buy_from_trader", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(tradeType, "buy_from_trader", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(actionName, "TradingConfirm", StringComparison.OrdinalIgnoreCase) &&
               (string.IsNullOrWhiteSpace(tradeType) || string.Equals(tradeType, "buy_from_trader", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetActionName(JsonObject actionNode)
    {
        return GetStringMember(actionNode, "Action", "action", "ActionType", "type");
    }

    private static string? GetStringMember(JsonObject actionNode, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetDeepPropertyValue(actionNode, key, out var value) || value is null)
            {
                continue;
            }

            var text = value.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static bool TryResolveTraderIdFromOwners(JsonObject actionNode, out string traderId)
    {
        traderId = string.Empty;

        if (TryGetNode(actionNode, out var toOwner, "ToOwner", "toOwner") &&
            TryResolveTraderOwnerId(toOwner, out traderId))
        {
            return true;
        }

        if (TryGetNode(actionNode, out var fromOwner, "FromOwner", "fromOwner") &&
            TryResolveTraderOwnerId(fromOwner, out traderId))
        {
            return true;
        }

        return false;
    }

    private static bool TryResolveTraderOwnerId(JsonNode? ownerNode, out string traderId)
    {
        traderId = string.Empty;
        if (ownerNode is not JsonObject ownerObj)
        {
            return false;
        }

        var ownerType = GetStringMember(ownerObj, "Type", "type");
        var ownerId = GetStringMember(ownerObj, "Id", "id");
        if (!string.Equals(ownerType, "Trader", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(ownerId))
        {
            return false;
        }

        traderId = ownerId;
        return true;
    }

    private static bool TryGetNode(JsonObject node, out JsonNode? value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetPropertyValue(node, key, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetDeepPropertyValue(JsonNode? node, string key, out JsonNode? value, int depth = 0)
    {
        value = null;
        if (node is null || depth > 24)
        {
            return false;
        }

        if (node is JsonObject obj)
        {
            if (TryGetPropertyValue(obj, key, out value))
            {
                return true;
            }

            foreach (var (_, child) in obj)
            {
                if (TryGetDeepPropertyValue(child, key, out value, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (TryGetDeepPropertyValue(child, key, out value, depth + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetPropertyValue(JsonObject node, string key, out JsonNode? value)
    {
        if (node.TryGetPropertyValue(key, out value))
        {
            return true;
        }

        foreach (var (name, child) in node)
        {
            if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = child;
            return true;
        }

        value = null;
        return false;
    }

    private static IEnumerable<(JsonObject Item, JsonArray ParentArray)> EnumerateNewItemsWithParent(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj)
            {
                if (string.Equals(key, "items", StringComparison.OrdinalIgnoreCase) && value is JsonObject itemsObj)
                {
                    if (itemsObj.TryGetPropertyValue("new", out var newNode) && newNode is JsonArray newItems)
                    {
                        foreach (var itemNode in newItems.OfType<JsonObject>())
                        {
                            yield return (itemNode, newItems);
                        }
                    }
                }

                if (value is not null)
                {
                    foreach (var nested in EnumerateNewItemsWithParent(value))
                    {
                        yield return nested;
                    }
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var entry in array)
            {
                if (entry is null)
                {
                    continue;
                }

                foreach (var nested in EnumerateNewItemsWithParent(entry))
                {
                    yield return nested;
                }
            }
        }
    }

    private static Dictionary<string, int> BuildOutputAmmoCounts(List<(JsonObject Item, JsonArray ParentArray)> entries)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (itemNode, _) in entries)
        {
            var tpl = GetItemTemplate(itemNode);
            if (string.IsNullOrWhiteSpace(tpl) || !Helper.IsAmmo(tpl) || Helper.IsAmmoBox(tpl))
            {
                continue;
            }

            if (!result.TryGetValue(tpl, out var count))
            {
                count = 0;
            }

            result[tpl] = count + 1;
        }

        return result;
    }

    private static string? GetItemTemplate(JsonObject itemNode)
    {
        return itemNode["_tpl"]?.ToString()
               ?? itemNode["tpl"]?.ToString()
               ?? itemNode["Template"]?.ToString();
    }

    private static int GetStackCount(JsonObject itemNode)
    {
        if (itemNode["upd"] is not JsonObject upd)
        {
            return 1;
        }

        var count = GetIntValue(upd["StackObjectsCount"]);
        return count ?? 1;
    }

    private static void SetStackCount(JsonObject itemNode, int count)
    {
        var upd = itemNode["upd"] as JsonObject ?? new JsonObject();
        itemNode["upd"] = upd;
        upd["StackObjectsCount"] = count;
    }

    private static JsonObject CloneItemNode(JsonObject itemNode, string newId, int count)
    {
        var clone = itemNode.DeepClone() as JsonObject ?? new JsonObject();
        clone["_id"] = newId;
        SetStackCount(clone, count);
        clone.Remove("location");
        return clone;
    }

    private static List<int> BuildStackCounts(int totalCount, int stackSize)
    {
        var result = new List<int>();
        if (stackSize <= 0)
        {
            result.Add(totalCount);
            return result;
        }

        var remaining = totalCount;
        while (remaining > 0)
        {
            var count = Math.Min(stackSize, remaining);
            result.Add(count);
            remaining -= count;
        }

        return result;
    }

    private static MongoId GenerateId()
    {
        return Helper.GenerateMongoId();
    }

    private static int ResolveAmmoStackCount(string templateId)
    {
        var maxStack = Helper.GetStackMaxSize(templateId, AmmoBatchSize);
        var count = Math.Min(AmmoBatchSize, maxStack);
        return count < 1 ? 1 : count;
    }

    private static int ResolveContainerCapacity(string? parentId, List<Item>? inventoryItems, DatabaseTables databaseTables)
    {
        if (string.IsNullOrWhiteSpace(parentId) || inventoryItems is null)
        {
            return 0;
        }

        var parentItem = inventoryItems.FirstOrDefault(item =>
            string.Equals(item.Id.ToString(), parentId, StringComparison.OrdinalIgnoreCase));
        if (parentItem is null)
        {
            return 0;
        }

        if (!databaseTables.Templates.Items.TryGetValue(parentItem.Template, out var template))
        {
            return 0;
        }

        return ResolveContainerCapacity(template);
    }

    private static int ResolveContainerCapacity(TemplateItem template)
    {
        var props = template.Properties;
        if (props is null)
        {
            return 0;
        }

        var cartridgeCapacity = ResolveSlotsCapacity(props.Cartridges);
        if (cartridgeCapacity > 0)
        {
            return cartridgeCapacity;
        }

        return ResolveSlotsCapacity(props.Chambers);
    }

    private static int ResolveSlotsCapacity(IEnumerable<Slot>? slots)
    {
        if (slots is null)
        {
            return 0;
        }

        foreach (var slot in slots)
        {
            if (slot is null)
            {
                continue;
            }

            var maxValue = slot.MaxCount;
            if (!maxValue.HasValue)
            {
                continue;
            }

            var rounded = (int)Math.Round(maxValue.Value, MidpointRounding.AwayFromZero);
            if (rounded > 0)
            {
                return rounded;
            }
        }

        return 0;
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

    private static Dictionary<string, TradeCountData> BuildTradeCounts(IReadOnlyList<JsonObject> actions, DatabaseTables databaseTables)
    {
        var result = new Dictionary<string, TradeCountData>(StringComparer.OrdinalIgnoreCase);

        foreach (var action in actions)
        {
            var actionName = GetActionName(action);
            var tradeType = GetStringMember(action, "type", "Type");
            if (!IsBuyFromTraderAction(actionName, tradeType))
            {
                continue;
            }

            var itemId = GetStringMember(action, "item_id", "itemId", "itemID", "ItemId", "ItemID");
            if (string.IsNullOrWhiteSpace(itemId))
            {
                continue;
            }

            var traderId = GetStringMember(action, "tid", "Tid", "traderId", "TraderId");
            if (string.Equals(traderId, "ragfair", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var count = GetIntMember(action, "count", "Count") ?? 0;
            var requestedCount = count;
            var maxAllowed = count;

            var tplCandidate = GetStringMember(action, "item_template_id", "itemTemplateId", "itemTemplateID", "tpl", "Tpl", "templateId", "TemplateId");
            if (string.IsNullOrWhiteSpace(tplCandidate))
            {
                tplCandidate = itemId;
            }

            string? tpl = null;
            if (!string.IsNullOrWhiteSpace(tplCandidate) &&
                MongoId.IsValidMongoId(tplCandidate) &&
                databaseTables.Templates.Items.ContainsKey(new MongoId(tplCandidate)))
            {
                tpl = tplCandidate;
            }

            Trader? trader = null;
            if (!string.IsNullOrWhiteSpace(traderId) && MongoId.IsValidMongoId(traderId))
            {
                databaseTables.Traders.TryGetValue(new MongoId(traderId), out trader);
            }

            Item? assortItem = null;
            if (trader is not null)
            {
                if (MongoId.IsValidMongoId(itemId))
                {
                    assortItem = trader.Assort?.Items?.FirstOrDefault(item =>
                        string.Equals(item.Id.ToString(), itemId, StringComparison.OrdinalIgnoreCase));
                }

                if (assortItem is null && !string.IsNullOrWhiteSpace(tpl))
                {
                    assortItem = trader.Assort?.Items?.FirstOrDefault(item =>
                        string.Equals(item.ParentId, "hideout", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(item.Template.ToString(), tpl, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (assortItem is null && MongoId.IsValidMongoId(itemId))
            {
                if (TryResolveAssortItemById(databaseTables, itemId, out var resolvedTrader, out var resolvedAssort))
                {
                    trader = resolvedTrader;
                    assortItem = resolvedAssort;
                    traderId = resolvedTrader.Base.Id.ToString();
                }
            }

            if (assortItem is not null)
            {
                tpl = assortItem.Template.ToString();
                maxAllowed = ResolveMaxAllowed(assortItem, count);

                var overrideKey = $"{traderId}:{tpl}";
                if (ModContext.Current.AssortOverrides.TryGetValue(overrideKey, out var data))
                {
                    assortItem.Upd ??= new Upd();
                    assortItem.Upd.StackObjectsCount = data.StackCount;
                    assortItem.Upd.UnlimitedCount = true;
                    assortItem.Upd.BuyRestrictionMax = data.BuyRestriction;
                    var restrictionCurrent = Convert.ToInt32(assortItem.Upd.BuyRestrictionCurrent);
                    if (restrictionCurrent > data.BuyRestriction)
                    {
                        assortItem.Upd.BuyRestrictionCurrent = data.BuyRestriction;
                    }
                }
            }

            if (assortItem is not null && trader is not null)
            {
                var schemeCount = ResolveSchemeRequestedCount(action, trader, assortItem);
                if (schemeCount > requestedCount)
                {
                    requestedCount = schemeCount;
                }
            }

            if (requestedCount < 1)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(tpl))
            {
                continue;
            }

            if (!result.TryGetValue(tpl, out var current))
            {
                current = new TradeCountData();
            }

            current.Requested += requestedCount;
            current.MaxAllowed += maxAllowed;
            result[tpl] = current;
        }

        return result;
    }

    private static bool TryResolveAssortItem(DatabaseTables databaseTables, string traderId, string itemId, out Item assortItem)
    {
        assortItem = null!;
        if (!MongoId.IsValidMongoId(traderId) || !MongoId.IsValidMongoId(itemId))
        {
            return false;
        }

        if (!databaseTables.Traders.TryGetValue(new MongoId(traderId), out var trader))
        {
            return false;
        }

        var assortItems = trader.Assort?.Items;
        if (assortItems is null)
        {
            return false;
        }

        assortItem = assortItems.FirstOrDefault(item =>
            string.Equals(item.Id.ToString(), itemId, StringComparison.OrdinalIgnoreCase));
        return assortItem is not null;
    }

    private static bool TryResolveAssortItemById(DatabaseTables databaseTables, string itemId, out Trader trader, out Item assortItem)
    {
        trader = null!;
        assortItem = null!;
        if (!MongoId.IsValidMongoId(itemId))
        {
            return false;
        }

        foreach (var entry in databaseTables.Traders)
        {
            var currentTrader = entry.Value;
            var assortItems = currentTrader.Assort?.Items;
            if (assortItems is null)
            {
                continue;
            }

            var found = assortItems.FirstOrDefault(item =>
                string.Equals(item.Id.ToString(), itemId, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                continue;
            }

            trader = currentTrader;
            assortItem = found;
            return true;
        }

        return false;
    }

    private static int ResolveMaxAllowed(Item assortItem, int requestedCount)
    {
        var upd = assortItem.Upd;
        if (upd?.UnlimitedCount == true)
        {
            var max = Convert.ToInt32(upd?.BuyRestrictionMax);
            if (max > 0)
            {
                var current = Convert.ToInt32(upd?.BuyRestrictionCurrent);
                var remaining = max - current;
                return remaining > 0 ? remaining : 0;
            }

            return requestedCount;
        }

        var stackCount = Convert.ToInt32(upd?.StackObjectsCount);
        if (stackCount > 0)
        {
            return stackCount;
        }

        return requestedCount;
    }

    private static int ResolveSchemeRequestedCount(JsonObject actionNode, Trader trader, Item assortItem)
    {
        var schemeId = GetIntMember(actionNode, "scheme_id", "schemeId", "SchemeId") ?? 0;
        if (schemeId < 0)
        {
            schemeId = 0;
        }

        if (!TryGetNode(actionNode, out var schemeItemsNode, "scheme_items", "schemeItems", "SchemeItems") ||
            schemeItemsNode is not JsonNode schemeNode)
        {
            return 0;
        }

        var totalCost = 0;
        foreach (var schemeEntry in EnumerateObjectEntries(schemeNode))
        {
            var entryCount = GetIntMember(schemeEntry, "count", "Count");
            if (entryCount is not null && entryCount.Value > 0)
            {
                totalCost += entryCount.Value;
            }
        }

        if (totalCost < 1)
        {
            return 0;
        }

        var barterScheme = trader.Assort?.BarterScheme;
        if (barterScheme is null || !barterScheme.TryGetValue(assortItem.Id, out var schemes))
        {
            return 0;
        }

        var schemeIndex = schemeId < schemes.Count ? schemeId : 0;
        if (schemeIndex >= schemes.Count)
        {
            return 0;
        }

        var scheme = schemes[schemeIndex];
        if (scheme is null || scheme.Count == 0)
        {
            return 0;
        }

        var pricePerStack = 0;
        foreach (var entry in scheme)
        {
            var templateId = entry.Template.ToString();
            if (!Helper.IsMoney(templateId))
            {
                return 0;
            }

            var entryCount = Convert.ToInt32(entry.Count);
            if (entryCount > 0)
            {
                pricePerStack += entryCount;
            }
        }

        if (pricePerStack < 1)
        {
            return 0;
        }

        var estimated = totalCost / pricePerStack;
        if (estimated < 1)
        {
            estimated = 1;
        }

        return estimated;
    }

    private static int? GetIntMember(JsonObject actionNode, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetDeepPropertyValue(actionNode, key, out var value) || value is null)
            {
                continue;
            }

            if (value is JsonValue jsonValue)
            {
                var intValue = GetIntValue(jsonValue);
                if (intValue is not null)
                {
                    return intValue.Value;
                }

                var textValue = jsonValue.ToString();
                if (int.TryParse(textValue, out var parsed))
                {
                    return parsed;
                }

                continue;
            }

            if (int.TryParse(value.ToString(), out var fallback))
            {
                return fallback;
            }
        }

        return null;
    }

    private static IEnumerable<JsonObject> EnumerateObjectEntries(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (var entry in array)
            {
                if (entry is JsonObject obj)
                {
                    yield return obj;
                }
            }

            yield break;
        }

        if (node is not JsonObject map)
        {
            yield break;
        }

        var yieldedChild = false;
        foreach (var (_, child) in map)
        {
            if (child is not JsonObject childObj)
            {
                continue;
            }

            yieldedChild = true;
            yield return childObj;
        }

        if (!yieldedChild)
        {
            yield return map;
        }
    }

    private sealed class TradeCountData
    {
        public int Requested { get; set; }
        public int MaxAllowed { get; set; }

        public int GetEffectiveCount()
        {
            if (Requested <= 0)
            {
                return 0;
            }

            if (MaxAllowed <= 0)
            {
                return Requested;
            }

            return Math.Min(Requested, MaxAllowed);
        }
    }
}
