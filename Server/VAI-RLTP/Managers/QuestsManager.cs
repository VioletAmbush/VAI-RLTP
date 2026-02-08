
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Json;

namespace VAI.RLTP.Managers;

public sealed record QuestRewardRequest(
    string QuestId,
    int LoyaltyLevel,
    string TraderId,
    string ItemId,
    string? TemplateId,
    PresetsManager.PresetData? PresetData,
    string QuestState);

[Injectable(InjectionType.Singleton)]
public sealed class QuestsManager(
    PresetsManager presetsManager,
    WeaponsManager weaponsManager,
    LocaleManager localeManager,
    ConfigServer configServer) : AbstractModManager
{
    private readonly PresetsManager _presetsManager = presetsManager;
    private readonly WeaponsManager _weaponsManager = weaponsManager;
    private readonly LocaleManager _localeManager = localeManager;
    private readonly QuestConfig _questConfig = configServer.GetConfig<QuestConfig>();

    private bool _readyToSetUnlocks;
    private readonly List<QuestRewardRequest> _setRequestQueue = [];

    protected override string ConfigName => "QuestsConfig";

    public override int Priority => 3;

    protected override void AfterPostDb()
    {
        var questCount = 0;

        if (GetConfigBool("removeRewards"))
        {
            RemoveDefaultRewards();
        }

        var questOverrides = GetConfigObject("quests");
        if (questOverrides != null)
        {
            foreach (var (questId, questNode) in questOverrides)
            {
                if (questNode is not JsonObject questConfig)
                {
                    continue;
                }

                var quest = GetQuestById(questId);
                if (quest is null)
                {
                    continue;
                }

                if (questConfig["clearAllRewards"]?.GetValue<bool>() == true || questConfig["rewards"] is JsonArray)
                {
                    SetQuestRewards(quest, questConfig["rewards"] as JsonArray, questConfig["clearAllRewards"]?.GetValue<bool>() == true);
                }

                var questDescription = GetStringValue(questConfig["description"]) ?? string.Empty;

                if (questConfig["requirements"] is JsonArray requirements && requirements.Count > 0)
                {
                    var mapped = requirements.OfType<JsonObject>().Select(req => new JsonObject
                    {
                        ["parent"] = req["type"]?.DeepClone(),
                        ["type"] = req["type"]?.DeepClone(),
                        ["count"] = req["count"]?.DeepClone(),
                        ["target"] = req["templateId"]?.DeepClone(),
                        ["location"] = "any",
                        ["weaponIds"] = req["weaponIds"]?.DeepClone(),
                        ["weaponCategories"] = req["weaponCategories"]?.DeepClone(),
                        ["scavTypes"] = req["scavTypes"]?.DeepClone()
                    }).ToList();

                    // 4.0 behavior (disabled): skip description updates for quest overrides to avoid fragile quest locales.
                    // SetQuestFinishReqs(quest, string.Empty, mapped, false);
                    SetQuestFinishReqs(quest, questDescription, mapped, true);
                }
                else if (questConfig["description"] != null)
                {
                    var descKeyText = GetQuestLocaleKey(quest, "description");
                    if (!string.IsNullOrWhiteSpace(descKeyText))
                    {
                        // 4.0 behavior (disabled): no locale update when there are no requirements.
                        // (left as reference; 3.11 always writes the description locale if provided)
                        // SetLocaleAll(descKeyText, questDescription);
                        SetLocaleAll(descKeyText, questDescription);
                    }
                }
            }
        }

        var mastery = GetConfigObject("masteryQuests");
        if (mastery != null)
        {
            foreach (var (title, node) in mastery)
            {
                if (node is not JsonObject questConfig)
                {
                    continue;
                }

                var questId = GetStringValue(questConfig["id"]);
                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                var quest = SetMasteryQuest(questConfig, title);
                if (quest is null)
                {
                    continue;
                }

                if (questConfig["startQuestRequirements"] is JsonArray startReqs)
                {
                    SetQuestStartReqs(quest, questConfig, startReqs);
                }

                EnsureQuestHasStartCondition(quest);
                SetMasteryQuestFinishReqs(quest, questConfig);
                SetQuestRewards(quest, questConfig["rewards"] as JsonArray, questConfig["clearAllRewards"]?.GetValue<bool>() == true);

                AddQuestToDb(questId, quest);

                questCount++;
            }
        }

        var collector = GetConfigObject("collectorQuests");
        if (collector != null)
        {
            foreach (var (title, node) in collector)
            {
                if (node is not JsonObject questConfig)
                {
                    continue;
                }

                var questId = GetStringValue(questConfig["id"]);
                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                var quest = SetCollectorQuest(questConfig, title);
                if (quest is null)
                {
                    continue;
                }

                if (questConfig["startQuestRequirements"] is JsonArray startReqs)
                {
                    SetQuestStartReqs(quest, questConfig, startReqs);
                }

                EnsureQuestHasStartCondition(quest);
                SetCollectorQuestFinishReqs(quest, questConfig);
                SetQuestRewards(quest, questConfig["rewards"] as JsonArray, questConfig["clearAllRewards"]?.GetValue<bool>() == true);

                AddQuestToDb(questId, quest);

                questCount++;
            }
        }

        if (Constants.EasyQuests)
        {
            foreach (var quest in DatabaseTables.Templates.Quests.Values)
            {
                SetQuestFinishReqs(quest, string.Empty, [new JsonObject
                {
                    ["parent"] = "HandoverItem",
                    ["type"] = "HandoverItem",
                    ["target"] = "5449016a4bdc2d6f028b456f",
                    ["location"] = "any",
                    ["count"] = 1
                }]);
            }
        }

        SyncQuestLocales();

        _readyToSetUnlocks = true;
        foreach (var request in _setRequestQueue)
        {
            SetQuestUnlockReward(request);
        }

        if (Constants.PrintQuestCount)
        {
            Constants.GetLogger().Info($"{Constants.ModTitle}: Quest changes applied! ({questCount} quests added)");
        }
        else
        {
            Constants.GetLogger().Info($"{Constants.ModTitle}: Quest changes applied!");
        }
    }

    protected override void AfterPostSpt()
    {
        if (GetConfigBool("disableRepeatableQuests"))
        {
            foreach (var repeatable in _questConfig.RepeatableQuests)
            {
                repeatable.MinPlayerLevel = 100;
            }
        }
    }

    private void RemoveDefaultRewards()
    {
        foreach (var quest in DatabaseTables.Templates.Quests.Values)
        {
            if (!quest.Rewards.TryGetValue("Success", out var success) || success is null)
            {
                continue;
            }

            success.RemoveAll(reward => reward.Type == RewardType.Item
                                        || reward.Type == RewardType.ProductionScheme
                                        || reward.Type == RewardType.AssortmentUnlock);
        }
    }

    private Quest? SetMasteryQuest(JsonObject questConfig, string questTitle)
    {
        var questId = GetStringValue(questConfig["id"]);
        if (string.IsNullOrWhiteSpace(questId))
        {
            return null;
        }

        if (GetQuestById(questId) != null)
        {
            Constants.GetLogger().Error($"{Constants.ModTitle}: Quest with id {questId} already exists");
            return null;
        }

        var questDescription = GetStringValue(questConfig["description"]) ?? string.Empty;
        var quest = BuildQuest(questId, GetStringValue(questConfig["traderAcr"]), QuestTypeEnum.Elimination, "/files/quest/icon/5968ec2986f7741ddf17db83.png", questTitle);
        SetQuestLocaleDefaults(questId, questTitle, questDescription);
        return quest;
    }

    private Quest? SetCollectorQuest(JsonObject questConfig, string questTitle)
    {
        var questId = GetStringValue(questConfig["id"]);
        if (string.IsNullOrWhiteSpace(questId))
        {
            return null;
        }

        if (GetQuestById(questId) != null)
        {
            Constants.GetLogger().Error($"{Constants.ModTitle}: Quest with id {questId} already exists");
            return null;
        }

        var questDescription = GetStringValue(questConfig["description"]) ?? string.Empty;
        var quest = BuildQuest(questId, GetStringValue(questConfig["traderAcr"]), QuestTypeEnum.PickUp, "/files/quest/icon/60c37450de6b0b44cc320e9a.jpg", questTitle);
        SetQuestLocaleDefaults(questId, questTitle, questDescription);
        return quest;
    }

    private Quest BuildQuest(string questId, string? traderAcr, QuestTypeEnum type, string image, string questTitle)
    {
        var traderId = Helper.AcronymToTraderId(traderAcr ?? string.Empty);
        var traderMongo = !string.IsNullOrWhiteSpace(traderId) && MongoId.IsValidMongoId(traderId)
            ? new MongoId(traderId)
            : MongoId.Empty();

        var conditions = new QuestConditionTypes
        {
            AvailableForFinish = [],
            AvailableForStart = [],
            Fail = []
        };

        // Match base quest shape: omit Started/Success when empty to avoid "active" UI glitches.
        conditions.Started = null;
        conditions.Success = null;

        return new Quest
        {
            Id = new MongoId(questId),
            TraderId = traderMongo,
            Type = type,

            CanShowNotificationsInGame = true,
            Restartable = false,
            InstantComplete = false,
            IsKey = false,
            SecretQuest = false,

            Status = 0,
            Image = image,
            Location = "any",
            Side = "Pmc",
            ProgressSource = "eft",
            AcceptanceAndFinishingSource = "eft",

            RankingModes = new List<string>(),
            GameModes = new List<string>(),
            ArenaLocations = new List<string>(),

            Rewards = new Dictionary<string, List<Reward>>
            {
                ["Started"] = [],
                ["Success"] = [],
                ["Fail"] = []
            },
            Conditions = conditions,

            Name = $"{questId} name",
            QuestName = questTitle,
            Description = $"{questId} description",

            Note = $"{questId} note",
            StartedMessageText = $"{questId} startedMessageText",
            AcceptPlayerMessage = $"{questId} acceptPlayerMessage",
            DeclinePlayerMessage = $"{questId} declinePlayerMessage",
            SuccessMessageText = $"{questId} successMessageText",
            FailMessageText = $"{questId} failMessageText",
            ChangeQuestMessageText = $"{questId} changeQuestMessageText",
            CompletePlayerMessage = $"{questId} completePlayerMessage"
        };
    }

    private void AddQuestToDb(string questId, Quest quest)
    {
        if (!MongoId.IsValidMongoId(questId))
        {
            return;
        }

        DatabaseTables.Templates.Quests[new MongoId(questId)] = quest;
    }

    private void SetQuestStartReqs(Quest quest, JsonObject questConfig, JsonArray startReqs)
    {
        if (Constants.NoNewQuestsStartRequirements)
        {
            return;
        }

        if (startReqs.Count == 0)
        {
            return;
        }

        quest.Conditions.AvailableForStart.Clear();

        var index = 0;
        foreach (var reqQuestNode in startReqs)
        {
            var reqQuestId = GetStringValue(reqQuestNode);
            if (string.IsNullOrWhiteSpace(reqQuestId))
            {
                continue;
            }

            var afsId = Helper.GenerateSha256Id($"{questConfig["id"]}AFS{index}");

            quest.Conditions.AvailableForStart.Add(new QuestCondition
            {
                Id = new MongoId(afsId),
                AvailableAfter = 0,
                Dispersion = 0,
                ConditionType = "Quest",
                GlobalQuestCounterId = string.Empty,
                DynamicLocale = false,
                Index = index,
                ParentId = string.Empty,
                Status = new HashSet<QuestStatusEnum> { QuestStatusEnum.Success },
                Target = new ListOrT<string>(null, reqQuestId),
                VisibilityConditions = []
            });

            index++;
        }
    }

    private static void EnsureQuestHasStartCondition(Quest quest)
    {
        if (Constants.NoNewQuestsStartRequirements)
        {
            return;
        }

        var conditions = quest.Conditions;
        if (conditions?.AvailableForStart == null || conditions.AvailableForStart.Count > 0)
        {
            return;
        }

        var afsId = Helper.GenerateSha256Id($"{quest.Id}AFSLevel");
        conditions.AvailableForStart.Add(new QuestCondition
        {
            Id = new MongoId(afsId),
            CompareMethod = ">=",
            ConditionType = "Level",
            DynamicLocale = false,
            GlobalQuestCounterId = string.Empty,
            Index = 0,
            ParentId = string.Empty,
            Value = 1,
            VisibilityConditions = []
        });
    }

    private void SetMasteryQuestFinishReqs(Quest quest, JsonObject questConfig)
    {
        var requirements = new List<JsonObject>
        {
            new()
            {
                ["type"] = QuestTypeEnum.Elimination.ToString(),
                ["count"] = questConfig["kills"]?.GetValue<int>() ?? 0,
                ["weaponIds"] = questConfig["weaponIds"]?.DeepClone(),
                ["weaponCategories"] = questConfig["weaponCategories"]?.DeepClone(),
                ["scavTypes"] = questConfig["scavTypes"]?.DeepClone()
            }
        };

        SetQuestFinishReqs(quest, GetStringValue(questConfig["description"]) ?? string.Empty, requirements);
    }

    private void SetCollectorQuestFinishReqs(Quest quest, JsonObject questConfig)
    {
        var requirements = new List<JsonObject>();
        if (questConfig["requirements"] is JsonArray reqArray)
        {
            foreach (var reqNode in reqArray)
            {
                if (reqNode is not JsonObject req)
                {
                    continue;
                }

                requirements.Add(new JsonObject
                {
                    ["templateId"] = req["templateId"]?.DeepClone(),
                    ["count"] = req["count"]?.DeepClone()
                });
            }
        }
        else if (questConfig["requirements"] is JsonObject reqs)
        {
            foreach (var (_, reqNode) in reqs)
            {
                if (reqNode is not JsonObject req)
                {
                    continue;
                }

                requirements.Add(new JsonObject
                {
                    ["templateId"] = req["templateId"]?.DeepClone(),
                    ["count"] = req["count"]?.DeepClone()
                });
            }
        }

        var finishReqs = requirements.Select(req => new JsonObject
        {
            ["parent"] = "HandoverItem",
            ["type"] = "HandoverItem",
            ["location"] = "any",
            ["target"] = req["templateId"]?.DeepClone(),
            ["count"] = req["count"]?.DeepClone()
        }).ToList();

        SetQuestFinishReqs(quest, GetStringValue(questConfig["description"]) ?? string.Empty, finishReqs);
    }

    private void SetQuestFinishReqs(Quest quest, string questDescription, List<JsonObject> finishRequirements, bool setDescription = true)
    {
        quest.Conditions.AvailableForFinish.Clear();

        if (finishRequirements.Count == 0)
        {
            return;
        }

        var index = 0;
        var description = questDescription ?? string.Empty;

        foreach (var reqConfig in finishRequirements)
        {
            var reqId = Helper.GenerateSha256Id($"{quest.Id}req{index}");

            var extraDescription = GetStringValue(reqConfig["description"]);
            if (!string.IsNullOrWhiteSpace(extraDescription))
            {
                description += $"{extraDescription}\n";
            }

            QuestCondition req;
            string? localeText = null;

            if (string.Equals(GetStringValue(reqConfig["type"]), QuestTypeEnum.Elimination.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                req = BuildEliminationRequirement(reqId, reqConfig, index, ref description, out localeText);
            }
            else if (string.Equals(GetStringValue(reqConfig["type"]), "HandoverItem", StringComparison.OrdinalIgnoreCase))
            {
                req = BuildHandoverRequirement(reqId, reqConfig, index, ref description, out localeText);
            }
            else
            {
                continue;
            }

            quest.Conditions.AvailableForFinish.Add(req);

            if (!string.IsNullOrWhiteSpace(localeText))
            {
                SetLocaleAll(reqId, localeText);
            }

            index++;
        }

        if (setDescription)
        {
            SetLocaleAll(GetQuestLocaleKey(quest, "description") ?? $"{quest.Id} description", description);
        }
    }

    private QuestCondition BuildEliminationRequirement(string reqId, JsonObject reqConfig, int index, ref string description, out string localeText)
    {
        localeText = "";

        var counterCondition = new QuestConditionCounterCondition
        {
            Id = new MongoId(Helper.GenerateSha256Id($"{reqId}_condition")),
            DynamicLocale = false,
            CompareMethod = ">=",
            Target = new ListOrT<string>(null, "Any"),
            Value = 1,
            ConditionType = "Kills",
            BodyPart = [],
            Daytime = new DaytimeCounter
            {
                From = 0,
                To = 0
            },
            Distance = new CounterConditionDistance
            {
                CompareMethod = ">=",
                Value = 0
            },
            EnemyEquipmentExclusive = new List<List<string>>(),
            EnemyEquipmentInclusive = new List<List<string>>(),
            EnemyHealthEffects = [],
            ResetOnSessionEnd = false,
            SavageRole = [],
            Weapon = new HashSet<string>(),
            WeaponCaliber = [],
            WeaponModsExclusive = new List<List<string>>(),
            WeaponModsInclusive = new List<List<string>>()
        };

        var req = new QuestCondition
        {
            Id = new MongoId(reqId),
            Type = QuestTypeEnum.Elimination.ToString(),
            ConditionType = "CounterCreator",
            Index = index,
            Value = reqConfig["count"]?.GetValue<int>() ?? 0,
            CompleteInSeconds = 0,
            DoNotResetIfCounterCompleted = false,
            DynamicLocale = false,
            GlobalQuestCounterId = string.Empty,
            OneSessionOnly = false,
            ParentId = string.Empty,
            VisibilityConditions = [],
            Counter = new QuestConditionCounter
            {
                Id = Helper.GenerateSha256Id($"{reqId}_counter"),
                Conditions = [counterCondition]
            }
        };

        var scavTypes = reqConfig["scavTypes"] as JsonArray;
        if (scavTypes != null && scavTypes.Count > 0)
        {
            var scavList = scavTypes.Select(node => GetStringValue(node) ?? string.Empty)
                .Where(val => !string.IsNullOrWhiteSpace(val))
                .ToList();
            localeText = $"Kill {string.Join(", ", scavList.Select(Helper.ScavTypeToString))}";
            description += $"{localeText}\n";

            counterCondition.SavageRole = scavList;
        }

        var weaponIds = reqConfig["weaponIds"] as JsonArray;
        if (weaponIds != null && weaponIds.Count > 0)
        {
            var weapons = weaponIds.Select(node => GetStringValue(node) ?? string.Empty)
                .Where(val => !string.IsNullOrWhiteSpace(val))
                .ToList();
            counterCondition.Weapon = new HashSet<string>(weapons);
            description += $"Kill with any of: {string.Join(", ", weapons.Select(_weaponsManager.GetWeaponDescription))}\n";
        }
        else if (reqConfig["weaponCategories"] is JsonArray categories && categories.Count > 0)
        {
            var ids = new List<(string Key, string Desc)>();
            foreach (var categoryNode in categories)
            {
                var category = GetStringValue(categoryNode);
                if (!string.IsNullOrWhiteSpace(category))
                {
                    ids.AddRange(_weaponsManager.GetWeaponCategoryIds(category));
                }
            }

            counterCondition.Weapon = new HashSet<string>(ids.Select(id => id.Key));
            description += $"Kill with any of: {string.Join(", ", ids.Select(id => id.Desc))}\n";
        }

        if (string.IsNullOrWhiteSpace(localeText))
        {
            localeText = "Kill any";
        }

        return req;
    }

    private QuestCondition BuildHandoverRequirement(string reqId, JsonObject reqConfig, int index, ref string description, out string localeText)
    {
        var target = GetStringValue(reqConfig["target"]) ?? string.Empty;
        var count = reqConfig["count"]?.GetValue<int>() ?? 0;

        var itemName = _localeManager.GetENLocale($"{target} Name");
        description += $"Find {count} {itemName}\n";
        localeText = $"Handover {count} {itemName}";

        return new QuestCondition
        {
            Id = new MongoId(reqId),
            Index = index,
            ConditionType = "HandoverItem",
            MaxDurability = 100,
            MinDurability = 0,
            DogtagLevel = 0,
            Value = count,
            OnlyFoundInRaid = false,
            Target = new ListOrT<string>(new List<string> { target }, null),
            DynamicLocale = false,
            IsEncoded = false,
            GlobalQuestCounterId = string.Empty,
            ParentId = string.Empty,
            VisibilityConditions = []
        };
    }

    private void SetQuestRewards(Quest quest, JsonArray? rewards, bool clearAllRewards)
    {
        if (!quest.Rewards.TryGetValue("Success", out var success) || success is null)
        {
            success = new List<Reward>();
            quest.Rewards["Success"] = success;
        }

        if (clearAllRewards)
        {
            success.Clear();
        }

        if (rewards is null || rewards.Count == 0)
        {
            return;
        }

        var index = success.Count == 0
            ? 0
            : success.Select(r => (int)(r.Index ?? 0)).DefaultIfEmpty(0).Max() + 1;

        foreach (var rewardConfig in rewards.OfType<JsonObject>())
        {
            var rewardItemId = Helper.GenerateSha256Id($"{quest.Id}reward{index}target");
            var templateId = rewardConfig["templateId"]?.ToString() ?? string.Empty;

            var reward = new Reward
            {
                Id = new MongoId(Helper.GenerateSha256Id($"{quest.Id}reward{index}")),
                Value = rewardConfig["count"]?.GetValue<int>() ?? 0,
                Type = ParseRewardType(rewardConfig["type"]?.ToString()) ?? RewardType.Item,
                Index = index,
                Target = rewardItemId,
                AvailableInGameEditions = new HashSet<string>(),
                GameMode = new[] { "regular", "pve" },
                IsHidden = false,
                IsEncoded = false,
                Unknown = false,
                FindInRaid = false,
                Items = []
            };

            if (!string.IsNullOrWhiteSpace(templateId) && MongoId.IsValidMongoId(templateId))
            {
                var count = rewardConfig["count"]?.GetValue<int>() ?? 0;
                reward.Items.Add(new Item
                {
                    Id = new MongoId(rewardItemId),
                    Template = new MongoId(templateId),
                    Upd = new Upd
                    {
                        StackObjectsCount = count
                    }
                });
            }

            success.Add(reward);
            index++;
        }
    }

    public void SetQuestUnlockReward(QuestRewardRequest request)
    {
        if (!_readyToSetUnlocks)
        {
            _setRequestQueue.Add(request);
            return;
        }

        var quest = GetQuestById(request.QuestId);
        if (quest is null)
        {
            return;
        }

        var rewards = GetQuestRewardsList(quest, request.QuestState);
        if (rewards is null)
        {
            return;
        }

        var index = rewards.Count == 0
            ? 0
            : rewards.Select(r => (int)(r.Index ?? 0)).DefaultIfEmpty(0).Max() + 1;

        var items = request.PresetData != null
            ? ConvertPresetItems(request.PresetData.Items)
            : new List<Item>
            {
                new()
                {
                    Id = new MongoId(request.ItemId),
                    Template = string.IsNullOrWhiteSpace(request.TemplateId) || !MongoId.IsValidMongoId(request.TemplateId)
                        ? MongoId.Empty()
                        : new MongoId(request.TemplateId)
                }
            };

        var reward = new Reward
        {
            AvailableInGameEditions = new HashSet<string>(),
            GameMode = new[] { "regular", "pve" },
            Id = new MongoId(Helper.GenerateSha256Id($"{quest.Id}reward{index}")),
            Index = index,
            IsHidden = false,
            Items = items,
            LoyaltyLevel = request.LoyaltyLevel,
            Target = request.ItemId,
            TraderId = request.TraderId,
            Type = RewardType.AssortmentUnlock,
            Unknown = false
        };

        rewards.Add(reward);
    }

    private Quest? GetQuestById(string questId)
    {
        if (!MongoId.IsValidMongoId(questId))
        {
            return null;
        }

        return DatabaseTables.Templates.Quests.TryGetValue(new MongoId(questId), out var quest)
            ? quest
            : null;
    }

    private static List<Reward>? GetQuestRewardsList(Quest quest, string questState)
    {
        var key = questState switch
        {
            "success" => "Success",
            "started" => "Started",
            _ => "Fail"
        };

        return quest.Rewards.TryGetValue(key, out var list) ? list : null;
    }

    private static RewardType? ParseRewardType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<RewardType>(value, true, out var parsed) ? parsed : null;
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

    private void SetQuestLocaleDefaults(string questId, string questTitle, string questDescription)
    {
        SetLocaleAll($"{questId} name", questTitle);
        SetLocaleAll($"{questId} description", questDescription);
        SetLocaleAll($"{questId} note", string.Empty);
        SetLocaleAll($"{questId} startedMessageText", $"Started {questTitle}");
        SetLocaleAll($"{questId} acceptPlayerMessage", $"Accepted {questTitle}");
        SetLocaleAll($"{questId} declinePlayerMessage", $"Declined {questTitle}");
        SetLocaleAll($"{questId} successMessageText", $"Succeeded {questTitle}");
        SetLocaleAll($"{questId} failMessageText", $"Failed {questTitle}");
        SetLocaleAll($"{questId} changeQuestMessageText", $"Changed {questTitle}");
        SetLocaleAll($"{questId} completePlayerMessage", $"Completed {questTitle}");
    }

    private void SetLocaleAll(string key, string value)
    {
        _localeManager.SetENLocale(key, value);
        _localeManager.SetENServerLocale(key, value);
    }

    private void SyncQuestLocales()
    {
        var locales = DatabaseTables.Locales;
        var globalLocales = locales.Global;
        var serverLocales = LocaleServerAccessor.GetOrNormalizeServerLocales(locales, nameof(QuestsManager));
        if (globalLocales is null || serverLocales is null)
        {
            return;
        }

        var languages = new HashSet<string>(globalLocales.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var key in serverLocales.Keys)
        {
            languages.Add(key);
        }

        foreach (var quest in DatabaseTables.Templates.Quests.Values)
        {
            var localeKeys = new[]
            {
                GetQuestLocaleKey(quest, "name"),
                GetQuestLocaleKey(quest, "description"),
                GetQuestLocaleKey(quest, "note"),
                GetQuestLocaleKey(quest, "startedMessageText"),
                GetQuestLocaleKey(quest, "acceptPlayerMessage"),
                GetQuestLocaleKey(quest, "declinePlayerMessage"),
                GetQuestLocaleKey(quest, "successMessageText"),
                GetQuestLocaleKey(quest, "failMessageText"),
                GetQuestLocaleKey(quest, "changeQuestMessageText"),
                GetQuestLocaleKey(quest, "completePlayerMessage")
            }.Where(key => !string.IsNullOrWhiteSpace(key)).ToList();

            if (localeKeys.Count == 0)
            {
                continue;
            }

            foreach (var language in languages)
            {
                if (!globalLocales.TryGetValue(language, out var globalLazy) || !serverLocales.TryGetValue(language, out var serverLazy))
                {
                    continue;
                }

                var globalLang = globalLazy.Value;
                var serverLang = serverLazy.Value;

                foreach (var localeKey in localeKeys)
                {
                    if (!globalLang.ContainsKey(localeKey) && serverLang.TryGetValue(localeKey, out var serverValue))
                    {
                        globalLang[localeKey] = serverValue;
                    }
                    else if (!serverLang.ContainsKey(localeKey) && globalLang.TryGetValue(localeKey, out var globalValue))
                    {
                        serverLang[localeKey] = globalValue;
                    }
                }
            }
        }
    }

    private static string? GetQuestLocaleKey(Quest quest, string key)
    {
        return key switch
        {
            "name" => quest.Name,
            "description" => quest.Description,
            "note" => quest.Note,
            "startedMessageText" => quest.StartedMessageText,
            "acceptPlayerMessage" => quest.AcceptPlayerMessage,
            "declinePlayerMessage" => quest.DeclinePlayerMessage,
            "successMessageText" => quest.SuccessMessageText,
            "failMessageText" => quest.FailMessageText,
            "changeQuestMessageText" => quest.ChangeQuestMessageText,
            "completePlayerMessage" => quest.CompletePlayerMessage,
            _ => null
        };
    }

    private static string? GetStringValue(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var str))
        {
            return str;
        }

        return node.ToString();
    }
}
