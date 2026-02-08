using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;
using VAI.RLTP.Routers;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class ProfilesManager(
    WipeManager wipeManager,
    PresetsManager presetsManager,
    LocaleManager localeManager,
    ICloner cloner,
    SaveServer saveServer) : AbstractModManager
{
    private readonly WipeManager _wipeManager = wipeManager;
    private readonly PresetsManager _presetsManager = presetsManager;
    private readonly LocaleManager _localeManager = localeManager;
    private readonly ICloner _cloner = cloner;
    private readonly SaveServer _saveServer = saveServer;
    private readonly HashSet<string> _customQuestIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _customQuestIdsLoaded;

    protected override string ConfigName => "ProfilesConfig";

    public override int Priority => 3;

    protected override void AfterPostDb()
    {
        var profilesConfig = GetConfigObject("profiles");
        if (profilesConfig is null)
        {
            return;
        }

        var profiles = DatabaseTables.Templates.Profiles;

        foreach (var (profileName, profileNode) in profilesConfig)
        {
            if (profileNode is not JsonObject profileConfig)
            {
                continue;
            }

            var copySource = profileConfig["copySource"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(copySource))
            {
                continue;
            }

            if (!profiles.TryGetValue(copySource, out var copy))
            {
                continue;
            }

            AddProfile(profileName, profileConfig, copy, profiles);
        }

        Constants.GetLogger().Info($"{Constants.ModTitle}: New profiles added!");
    }

    public async Task HandleProfileStatus(MongoId sessionId)
    {
        if (!IsEnabled())
        {
            return;
        }

        var profile = ModContext.Current.ProfileHelper.GetPmcProfile(sessionId);
        if (profile is null)
        {
            return;
        }

        var gameVersion = profile.Info?.GameVersion;
        var isRliteProfile = !string.IsNullOrWhiteSpace(gameVersion)
            && gameVersion.StartsWith("VAI Rogue-lite", StringComparison.OrdinalIgnoreCase);

        var dirty = false;

        if (isRliteProfile)
        {
            if (TrySetQuests(profile))
            {
                dirty = true;
            }
        }

        if (isRliteProfile)
        {
            if (TryFixStashLocations(profile))
            {
                dirty = true;
            }
        }

        if (dirty)
        {
            await _saveServer.SaveProfileAsync(sessionId);
        }
    }

    public List<Item> GetProfileBonus(string profileKey, string target, string parentId)
    {
        var profilesConfig = GetConfigObject("profiles");
        if (profilesConfig is null || !profilesConfig.TryGetPropertyValue(profileKey, out var profileNode) || profileNode is not JsonObject profileConfig)
        {
            return [];
        }

        var itemConfigs = new List<JsonObject>();
        if (target == "death" && profileConfig["deathItems"] is JsonArray deathItems)
        {
            itemConfigs.AddRange(deathItems.OfType<JsonObject>());
        }

        if (target == "survive" && profileConfig["surviveItems"] is JsonArray surviveItems)
        {
            itemConfigs.AddRange(surviveItems.OfType<JsonObject>());
        }

        var result = new List<Item>();

        foreach (var itemConfig in itemConfigs)
        {
            var templateId = itemConfig["templateId"]?.GetValue<string>();
            var presetId = itemConfig["presetId"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(templateId))
            {
                if (!MongoId.IsValidMongoId(templateId))
                {
                    continue;
                }

                var count = GetIntValue(itemConfig["count"]) ?? 1;
                var stackable = Helper.IsStackable(templateId);
                if (count <= 1 || stackable)
                {
                    var item = new Item
                    {
                        Id = GenerateId(),
                        Template = new MongoId(templateId),
                        ParentId = parentId,
                        SlotId = "hideout"
                    };

                    if (stackable)
                    {
                        item.Upd = new Upd
                        {
                            StackObjectsCount = count
                        };
                    }

                    result.Add(item);
                }
                else
                {
                    for (var i = 0; i < count; i++)
                    {
                        result.Add(new Item
                        {
                            Id = GenerateId(),
                            Template = new MongoId(templateId),
                            ParentId = parentId,
                            SlotId = "hideout",
                            Upd = new Upd
                            {
                                StackObjectsCount = 1
                            }
                        });
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(presetId))
            {
                var preset = _presetsManager.ResolvePreset(presetId, parentId);
                if (preset is null)
                {
                    continue;
                }

                var items = ConvertPresetItems(preset.Items);
                var root = items.FirstOrDefault(item => string.Equals(item.Id.ToString(), preset.RootId, StringComparison.OrdinalIgnoreCase));
                if (root?.Upd != null)
                {
                    root.Upd.StackObjectsCount = 1;
                }

                result.AddRange(items);
            }
        }

        if (target == "death" && profileConfig["gungame"] is JsonObject gungame && gungame["enabled"]?.GetValue<bool>() == true)
        {
            var categories = gungame["presetCategories"] as JsonArray;
            var exclude = gungame["excludePresets"] as JsonArray;
            var include = gungame["includePresets"] as JsonArray;

            var preset = _presetsManager.ResolveRandomPreset(
                categories?.Select(node => node?.ToString() ?? string.Empty).Where(val => !string.IsNullOrWhiteSpace(val)).ToList() ?? [],
                exclude?.Select(node => node?.ToString() ?? string.Empty).Where(val => !string.IsNullOrWhiteSpace(val)).ToList() ?? [],
                include?.Select(node => node?.ToString() ?? string.Empty).Where(val => !string.IsNullOrWhiteSpace(val)).ToList() ?? [],
                parentId);

            if (preset != null)
            {
                var items = ConvertPresetItems(preset.Items);
                var root = items.FirstOrDefault(item => string.Equals(item.Id.ToString(), preset.RootId, StringComparison.OrdinalIgnoreCase));
                if (root?.Upd != null)
                {
                    root.Upd.StackObjectsCount = 1;
                }

                result.AddRange(items);
            }
        }

        return result;
    }

    private void AddProfile(string profileName, JsonObject config, ProfileSides copySource, Dictionary<string, ProfileSides> profilesTable)
    {
        var profile = _cloner.Clone(copySource) as ProfileSides;
        if (profile is null)
        {
            return;
        }

        if (profile.Bear != null)
        {
            SetSide(profileName, config, profile.Bear);
        }

        if (profile.Usec != null)
        {
            SetSide(profileName, config, profile.Usec);
        }

        var description = config["description"]?.GetValue<string>() ?? string.Empty;
        var actualDescription = config["actualDescription"]?.GetValue<string>() ?? string.Empty;
        profile.DescriptionLocaleKey = $"{description}\n\n{actualDescription}";

        profilesTable[profileName] = profile;
    }

    private void SetSide(string profileName, JsonObject config, TemplateSide side)
    {
        var character = side.Character;
        if (character is null)
        {
            return;
        }

        _wipeManager.ClearProfileStash(character);

        var inventory = character.Inventory;
        var items = inventory.Items;
        var stashId = inventory.Stash?.ToString() ?? string.Empty;

        items.RemoveAll(item => string.Equals(item.ParentId, stashId, StringComparison.OrdinalIgnoreCase));

        if (config["clearAllItems"]?.GetValue<bool>() == true)
        {
            items.RemoveAll(item =>
            {
                var slotId = item.SlotId;
                var parentId = item.ParentId;
                return string.Equals(slotId, "Scabbard", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(slotId, "SecuredContainer", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(parentId, stashId, StringComparison.OrdinalIgnoreCase);
            });
        }

        if (character.Info != null)
        {
            character.Info.GameVersion = profileName;
        }

        var traderTemplate = side.Trader ?? new ProfileTraderTemplate();
        side.Trader = traderTemplate;

        if (traderTemplate.InitialLoyaltyLevel == null || traderTemplate.InitialLoyaltyLevel.Count == 0)
        {
            traderTemplate.InitialLoyaltyLevel = Helper.GetTradersRecords(1);
        }

        var initialStanding = traderTemplate.InitialStanding ?? new Dictionary<string, double?>();
        initialStanding["default"] = GetNumberValue(config["tradersStanding"]);
        traderTemplate.InitialStanding = initialStanding;

        traderTemplate.InitialSalesSum = 0;
        traderTemplate.JaegerUnlocked = config["jaegerUnlocked"]?.GetValue<bool>() ?? false;

        if (config["areas"] is JsonObject areasConfig)
        {
            var areas = character.Hideout?.Areas;
            if (areas != null)
            {
                foreach (var (areaKey, areaNode) in areasConfig)
                {
                    if (areaNode is not JsonObject areaConfig)
                    {
                        continue;
                    }

                    var level = GetIntValue(areaConfig["startingLevel"]) ?? 0;
                    var hasTypeId = int.TryParse(areaKey, out var typeId);
                    foreach (var area in areas)
                    {
                        if (hasTypeId ? (int)area.Type == typeId : string.Equals(area.Type.ToString(), areaKey, StringComparison.OrdinalIgnoreCase))
                        {
                            area.Level = level;
                            break;
                        }
                    }
                }
            }
        }

        if (config["items"] is JsonArray itemConfigs)
        {
            foreach (var itemConfig in itemConfigs.OfType<JsonObject>())
            {
                AddConfiguredItem(itemConfig, items, stashId);
            }
        }

        if (config["skills"] is JsonObject skillsConfig && character.Skills != null)
        {
            foreach (var (skillKey, skillNode) in skillsConfig)
            {
                if (skillNode is not JsonObject skillConfig)
                {
                    continue;
                }

                var startingLevel = GetIntValue(skillConfig["startingLevel"]) ?? 0;
                foreach (var skill in character.Skills.Common)
                {
                    if (string.Equals(skill.Id.ToString(), skillKey, StringComparison.OrdinalIgnoreCase))
                    {
                        skill.Progress = startingLevel;
                    }
                }
            }

            character.Skills.Mastering = new List<MasterySkill>();
            character.Skills.Points = 0;
        }

        Helper.FillLocations(character, DatabaseTables);
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

            if (value.TryGetValue<long>(out var longVal))
            {
                return longVal;
            }
        }

        return null;
    }

    private void AddConfiguredItem(JsonObject itemConfig, List<Item> items, string stashId)
    {
        var templateId = itemConfig["templateId"]?.GetValue<string>();
        var presetId = itemConfig["presetId"]?.GetValue<string>();

        if (!string.IsNullOrWhiteSpace(templateId))
        {
            if (!MongoId.IsValidMongoId(templateId))
            {
                return;
            }

            var count = GetIntValue(itemConfig["count"]) ?? 1;
            var parentId = itemConfig["parentId"]?.GetValue<string>() ?? stashId;
            var slotId = itemConfig["slotId"]?.GetValue<string>() ?? "hideout";
            var idValue = itemConfig["id"]?.GetValue<string>();
            var id = !string.IsNullOrWhiteSpace(idValue) && MongoId.IsValidMongoId(idValue)
                ? new MongoId(idValue)
                : GenerateId();

            if (count <= 1 || Helper.IsStackable(templateId))
            {
                var item = new Item
                {
                    Id = id,
                    Template = new MongoId(templateId),
                    ParentId = parentId,
                    SlotId = slotId
                };

                if (count > 1)
                {
                    item.Upd = new Upd
                    {
                        StackObjectsCount = count
                    };
                }

                items.Add(item);
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    items.Add(new Item
                    {
                        Id = GenerateId(),
                        Template = new MongoId(templateId),
                        ParentId = parentId,
                        SlotId = slotId,
                        Upd = new Upd
                        {
                            StackObjectsCount = 1
                        }
                    });
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(presetId))
        {
            var preset = _presetsManager.ResolvePreset(presetId, stashId);
            if (preset is null)
            {
                return;
            }

            var presetItems = ConvertPresetItems(preset.Items);
            var root = presetItems.FirstOrDefault(item => string.Equals(item.Id.ToString(), preset.RootId, StringComparison.OrdinalIgnoreCase));
            if (root?.Upd != null)
            {
                root.Upd.StackObjectsCount = 1;
            }

            items.AddRange(presetItems);
        }
    }

    private bool TrySetQuests(PmcData profile)
    {
        if (profile.Quests == null || profile.Quests.Count > 0)
        {
            return false;
        }

        var gameVersion = profile.Info?.GameVersion;
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return false;
        }

        var profilesConfig = GetConfigObject("profiles");
        if (profilesConfig is null)
        {
            return false;
        }

        if (!profilesConfig.TryGetPropertyValue(gameVersion, out JsonNode? profileNode) || profileNode is not JsonObject profileConfig)
        {
            return false;
        }

        if (profileConfig["completedQuests"] is not JsonArray completedQuests || completedQuests.Count == 0)
        {
            return false;
        }

        var questIds = completedQuests.Any(node => string.Equals(node?.ToString(), "__all__", StringComparison.OrdinalIgnoreCase))
            ? DatabaseTables.Templates.Quests.Keys.Select(key => key.ToString()).Where(val => !string.IsNullOrWhiteSpace(val)).ToList()
            : completedQuests.Select(node => node?.ToString() ?? string.Empty).Where(val => !string.IsNullOrWhiteSpace(val)).ToList();

        var added = false;
        foreach (var questId in questIds)
        {
            if (!MongoId.IsValidMongoId(questId))
            {
                Constants.GetLogger().Error($"{Constants.ModTitle}: Could not find quest with id {questId}");
                continue;
            }

            if (!DatabaseTables.Templates.Quests.TryGetValue(new MongoId(questId), out var quest))
            {
                Constants.GetLogger().Error($"{Constants.ModTitle}: Could not find quest with id {questId}");
                continue;
            }

            var completedConditions = quest.Conditions?.AvailableForFinish
                ?.Select(c => c.Id.ToString())
                .Where(val => !string.IsNullOrWhiteSpace(val))
                .ToList() ?? new List<string>();

            var questStatus = new QuestStatus
            {
                QId = quest.Id,
                StartTime = 0,
                Status = QuestStatusEnum.AvailableForFinish,
                StatusTimers = new Dictionary<QuestStatusEnum, double>(),
                CompletedConditions = completedConditions,
                AvailableAfter = 0
            };

            profile.Quests.Add(questStatus);
            added = true;
        }

        if (added)
        {
            Constants.GetLogger().Info($"{Constants.ModTitle}: Quests set for profile {profile.Info?.Nickname}");
        }

        return added;
    }

    private bool TryFixStashLocations(PmcData profile)
    {
        var inventory = profile.Inventory;
        if (inventory?.Items is null)
        {
            return false;
        }

        var stashId = inventory.Stash?.ToString();
        if (string.IsNullOrWhiteSpace(stashId))
        {
            return false;
        }

        var needsFix = inventory.Items.Any(item =>
            string.Equals(item.ParentId, stashId, StringComparison.OrdinalIgnoreCase) && item.Location is null);
        if (!needsFix)
        {
            return false;
        }

        Helper.FillLocations(profile, DatabaseTables);
        return true;
    }

    public bool NormalizeCustomQuestStatuses(PmcData? profile)
    {
        if (profile is null)
        {
            return false;
        }

        EnsureCustomQuestIdsLoaded();
        if (_customQuestIds.Count == 0 || profile.Quests == null || profile.Quests.Count == 0)
        {
            return false;
        }

        var availableStatus = (QuestStatusEnum)1;
        var startedStatus = (QuestStatusEnum)2;

        var dirty = false;
        foreach (var questStatus in profile.Quests)
        {
            var questId = questStatus.QId.ToString();
            if (string.IsNullOrWhiteSpace(questId) || !_customQuestIds.Contains(questId))
            {
                continue;
            }

            if (questStatus.Status != startedStatus)
            {
                continue;
            }

            if (questStatus.StartTime > 0)
            {
                continue;
            }

            var timers = questStatus.StatusTimers;
            if (timers is null || !timers.ContainsKey(availableStatus) || !timers.ContainsKey(startedStatus))
            {
                continue;
            }

            questStatus.Status = availableStatus;
            questStatus.StartTime = 0;
            questStatus.CompletedConditions = [];

            var availableTime = timers.TryGetValue(availableStatus, out var time) ? time : 0;
            questStatus.StatusTimers = new Dictionary<QuestStatusEnum, double>
            {
                [availableStatus] = availableTime
            };

            if (ResetQuestCounters(profile, questId))
            {
                dirty = true;
            }

            dirty = true;
        }

        return dirty;
    }

    private bool ResetQuestCounters(PmcData profile, string questId)
    {
        if (!MongoId.IsValidMongoId(questId))
        {
            return false;
        }

        if (!DatabaseTables.Templates.Quests.TryGetValue(new MongoId(questId), out var quest))
        {
            return false;
        }

        var conditions = quest.Conditions?.AvailableForFinish;
        if (conditions is null || conditions.Count == 0)
        {
            return false;
        }

        var counters = profile.TaskConditionCounters;
        if (counters is null || counters.Count == 0)
        {
            return false;
        }

        var conditionIds = new HashSet<string>(
            conditions.Select(c => c.Id.ToString()).Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);

        if (conditionIds.Count == 0)
        {
            return false;
        }

        var changed = false;
        foreach (var counter in counters.Values)
        {
            if (counter is null)
            {
                continue;
            }

            var sourceId = counter.SourceId;
            if (string.IsNullOrWhiteSpace(sourceId) || !conditionIds.Contains(sourceId))
            {
                continue;
            }

            if (counter.Value != 0)
            {
                counter.Value = 0;
                changed = true;
            }
        }

        return changed;
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

    private void EnsureCustomQuestIdsLoaded()
    {
        if (_customQuestIdsLoaded)
        {
            return;
        }

        _customQuestIdsLoaded = true;

        if (LoadConfig("QuestsConfig") is not JsonObject config)
        {
            return;
        }

        AddCustomQuestIds(config["masteryQuests"] as JsonObject);
        AddCustomQuestIds(config["collectorQuests"] as JsonObject);
    }

    public IReadOnlyCollection<string> GetCustomQuestIds()
    {
        EnsureCustomQuestIdsLoaded();
        return _customQuestIds;
    }

    private void AddCustomQuestIds(JsonObject? section)
    {
        if (section is null)
        {
            return;
        }

        foreach (var (_, node) in section)
        {
            if (node is not JsonObject questConfig)
            {
                continue;
            }

            var questId = questConfig["id"]?.ToString();
            if (!string.IsNullOrWhiteSpace(questId))
            {
                _customQuestIds.Add(questId);
            }
        }
    }

    private static MongoId GenerateId()
    {
        return Helper.GenerateMongoId();
    }
}
