using System.Collections;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class PresetsManager(LocaleManager localeManager) : AbstractModManager
{
    protected override string ConfigName => "PresetsConfig";
    private readonly Dictionary<string, string> _presetIdMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly LocaleManager _localeManager = localeManager;

    protected override void AfterPostDb()
    {
        var presetCount = 0;

        var items = GetConfigObject("items");
        if (items != null)
        {
            foreach (var (presetId, _) in items)
            {
                SetPreset(presetId);
                presetCount++;
            }
        }

        var categorized = GetConfigObject("categorizedItems");
        if (categorized != null)
        {
            foreach (var (_, categoryNode) in categorized)
            {
                if (categoryNode is not JsonObject categoryItems)
                {
                    continue;
                }

                foreach (var (presetId, _) in categoryItems)
                {
                    SetPreset(presetId);
                    presetCount++;
                }
            }
        }

        if (Constants.PrintPresetCount)
        {
            Constants.GetLogger().Info($"{Constants.ModTitle}: Added {presetCount} presets.");
        }
    }

    private void SetPreset(string presetId)
    {
        var preset = ResolvePreset(presetId);
        if (preset is null)
        {
            return;
        }

        var rootItem = preset.Items.FirstOrDefault(item =>
            string.Equals(item["_id"]?.ToString(), preset.RootId, StringComparison.OrdinalIgnoreCase));
        var presetKey = GetPresetKey(presetId, rootItem);

        var displayId = ReplaceFirstUnderscore(presetId).ToUpperInvariant();
        var changeName = !(displayId.Contains("MOUNTED", StringComparison.Ordinal) ||
                           displayId.Contains("SHADE", StringComparison.Ordinal) ||
                           displayId.Contains("EYECUP", StringComparison.Ordinal) ||
                           displayId.Contains("COLLIMATOR", StringComparison.Ordinal));

        var presetJson = new JsonObject
        {
            ["_changeWeaponName"] = changeName,
            ["_id"] = presetKey,
            ["_name"] = displayId,
            ["_parent"] = preset.RootId,
            ["_type"] = "Preset",
            ["_items"] = new JsonArray(preset.Items.Select(item => item.DeepClone()).ToArray())
        };

        if (IsBasicPreset(presetId))
        {
            var encyclopedia = rootItem?["_tpl"]?.ToString();
            if (!string.IsNullOrWhiteSpace(encyclopedia))
            {
                presetJson["_encyclopedia"] = encyclopedia;
            }
        }

        if (!MongoId.IsValidMongoId(presetKey) || !MongoId.IsValidMongoId(preset.RootId))
        {
            Constants.GetLogger().Warning($"{Constants.ModTitle}: Invalid preset id detected for {presetId}.");
            return;
        }

        var presetObject = JsonUtil.Deserialize<Preset>(presetJson.ToJsonString());
        if (presetObject is null)
        {
            Constants.GetLogger().Warning($"{Constants.ModTitle}: Failed to create preset {presetId}.");
            return;
        }

        DatabaseTables.Globals.ItemPresets[new MongoId(presetKey)] = presetObject;
        SetPresetLocale(presetKey, presetId, rootItem);
    }

    public PresetData? ResolveRandomPreset(IEnumerable<string> categories, IEnumerable<string>? excludedPresets, IEnumerable<string>? includedPresets, string parentId)
    {
        var categoryList = categories.ToList();
        var categorizedItems = GetConfigObject("categorizedItems");
        if (categorizedItems is null)
        {
            return null;
        }

        if (categoryList.Any(cat => string.Equals(cat, "__all__", StringComparison.OrdinalIgnoreCase)))
        {
            categoryList = categorizedItems.Select(kvp => kvp.Key).ToList();
        }

        if (categoryList.Any(cat => string.Equals(cat, "__all__weapon__", StringComparison.OrdinalIgnoreCase)))
        {
            categoryList = GetConfigArray("weaponCategories")?.Select(node => node?.GetValue<string>())
                .Where(val => !string.IsNullOrWhiteSpace(val))
                .ToList() ?? [];
        }

        if (categoryList.Any(cat => string.Equals(cat, "__all__armor__", StringComparison.OrdinalIgnoreCase)))
        {
            categoryList = GetConfigArray("armorCategories")?.Select(node => node?.GetValue<string>())
                .Where(val => !string.IsNullOrWhiteSpace(val))
                .ToList() ?? [];
        }

        if (categoryList.Any(cat => string.Equals(cat, "__all__equipment__", StringComparison.OrdinalIgnoreCase)))
        {
            categoryList = GetConfigArray("equipmentCategories")?.Select(node => node?.GetValue<string>())
                .Where(val => !string.IsNullOrWhiteSpace(val))
                .ToList() ?? [];
        }

        var presets = includedPresets?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [];

        foreach (var categoryName in categoryList)
        {
            if (categorizedItems.TryGetPropertyValue(categoryName, out var categoryNode) && categoryNode is JsonObject category)
            {
                foreach (var (presetKey, _) in category)
                {
                    if (!presets.Contains(presetKey))
                    {
                        presets.Add(presetKey);
                    }
                }
            }
        }

        if (excludedPresets != null && excludedPresets.Any())
        {
            presets = presets.Where(p => !excludedPresets.Contains(p)).ToList();
        }

        if (presets.Count == 0)
        {
            Constants.GetLogger().Error($"{Constants.ModTitle}: Could not find random preset!");
            return null;
        }

        var randomPreset = ResolvePreset(presets[GetRandomIndex(0, presets.Count - 1)], parentId);
        var failsafeCounter = 0;

        while (randomPreset is null)
        {
            failsafeCounter++;
            randomPreset = ResolvePreset(presets[GetRandomIndex(0, presets.Count - 1)], parentId);

            if (failsafeCounter > 10)
            {
                break;
            }
        }

        if (randomPreset is null)
        {
            Constants.GetLogger().Error($"{Constants.ModTitle}: Could not find random preset!");
            return null;
        }

        return randomPreset;
    }

    public List<string> ResolveAllPresetIds()
    {
        var result = new List<string>();
        var items = GetConfigObject("items");
        if (items != null)
        {
            result.AddRange(items.Select(kvp => kvp.Key));
        }

        var categorized = GetConfigObject("categorizedItems");
        if (categorized != null)
        {
            foreach (var (_, categoryNode) in categorized)
            {
                if (categoryNode is not JsonObject categoryItems)
                {
                    continue;
                }

                result.AddRange(categoryItems.Select(kvp => kvp.Key));
            }
        }

        return result;
    }

    public PresetData? ResolvePreset(string presetId, string? parentId = null)
    {
        JsonArray? presetConfig = null;

        var items = GetConfigObject("items");
        if (items != null && items.TryGetPropertyValue(presetId, out var itemNode))
        {
            presetConfig = itemNode as JsonArray;
        }

        if (presetConfig is null)
        {
            var categorized = GetConfigObject("categorizedItems");
            if (categorized != null)
            {
                foreach (var (_, categoryNode) in categorized)
                {
                    if (categoryNode is not JsonObject categoryItems)
                    {
                        continue;
                    }

                    if (categoryItems.TryGetPropertyValue(presetId, out var presetNode) && presetNode is JsonArray presetArray)
                    {
                        presetConfig = presetArray;
                        break;
                    }
                }
            }
        }

        if (presetConfig is null)
        {
            Constants.GetLogger().Error($"{Constants.ModTitle}: Could not find preset {presetId}");
            return null;
        }

        var preset = ResolvePresetImpl(presetConfig, parentId);
        if (preset is null)
        {
            return null;
        }

        preset = RegeneratePresetIds(preset);

        var rootItem = preset.Items.FirstOrDefault(item =>
            string.Equals(item["_id"]?.ToString(), preset.RootId, StringComparison.OrdinalIgnoreCase));
        if (rootItem != null)
        {
            GetPresetKey(presetId, rootItem);
            SetPresetDurability(presetId, rootItem);
        }

        return preset;
    }

    private PresetData? ResolvePresetImpl(JsonArray config, string? parentId = null)
    {
        JsonObject? presetRoot = null;

        foreach (var node in config)
        {
            if (node is not JsonObject item)
            {
                continue;
            }

            var parent = item["parentId"]?.ToString();
            if (string.IsNullOrWhiteSpace(parent) || !config.Any(entry => entry is JsonObject obj && obj["_id"]?.ToString() == parent))
            {
                presetRoot = item;
                break;
            }
        }

        if (presetRoot is null)
        {
            Constants.GetLogger().Error($"{Constants.ModTitle}: Could not find root item for preset {JsonSerializer.Serialize(config)}");
            return null;
        }

        var result = new List<JsonObject>();
        var rootItem = new JsonObject
        {
            ["_id"] = presetRoot["_id"]?.GetValue<string>() ?? string.Empty,
            ["_tpl"] = presetRoot["_tpl"]?.GetValue<string>() ?? string.Empty,
            ["slotId"] = "hideout"
        };

        if (presetRoot["upd"] is JsonNode updNode)
        {
            rootItem["upd"] = updNode.DeepClone();
        }

        if (!string.IsNullOrWhiteSpace(parentId))
        {
            rootItem["parentId"] = parentId;
        }

        result.Add(rootItem);

        foreach (var node in config)
        {
            if (node is not JsonObject item)
            {
                continue;
            }

            if (item["_tpl"]?.ToString() == presetRoot["_tpl"]?.ToString())
            {
                continue;
            }

            var newItem = new JsonObject
            {
                ["_id"] = item["_id"]?.GetValue<string>() ?? string.Empty,
                ["_tpl"] = item["_tpl"]?.GetValue<string>() ?? string.Empty,
                ["slotId"] = item["slotId"]?.GetValue<string>() ?? string.Empty,
                ["parentId"] = item["parentId"]?.GetValue<string>() ?? string.Empty
            };

            if (item["upd"] is JsonNode upd)
            {
                newItem["upd"] = upd.DeepClone();
            }

            result.Add(newItem);
        }

        return new PresetData(rootItem["_id"]?.ToString() ?? string.Empty, result);
    }

    private void SetPresetDurability(string presetId, JsonObject presetRoot)
    {
        if (GetConfigBool("alwaysFullDurability"))
        {
            SetRepairable(presetRoot, 100, 100);
            return;
        }

        var upd = presetRoot["upd"] as JsonObject;
        if (upd != null && upd["Repairable"] != null)
        {
            return;
        }

        var id = presetId.ToLowerInvariant();

        if (id.Contains("basic"))
        {
            SetRepairable(presetRoot, GetConfigInt("basicDurability", 30), GetConfigInt("basicMaxDurability", 100));
            return;
        }

        if (id.Contains("std"))
        {
            SetRepairable(presetRoot, GetConfigInt("stdDurability", 50), GetConfigInt("stdMaxDurability", 100));
            return;
        }

        if (id.Contains("adv"))
        {
            SetRepairable(presetRoot, GetConfigInt("advDurability", 80), GetConfigInt("advMaxDurability", 100));
            return;
        }

        if (id.Contains("sup"))
        {
            SetRepairable(presetRoot, GetConfigInt("supDurability", 100), GetConfigInt("supMaxDurability", 100));
            return;
        }

        if (id.Contains("master"))
        {
            SetRepairable(presetRoot, GetConfigInt("masterDurability", 100), GetConfigInt("masterMaxDurability", 100));
            return;
        }

        if (id.Contains("melee barter"))
        {
            SetRepairable(presetRoot, GetConfigInt("meleeBarterDurability", 80), GetConfigInt("meleeBarterMaxDurability", 100));
            return;
        }

        if (id.Contains("contractor"))
        {
            SetRepairable(presetRoot, GetConfigInt("contractorDurability", 80), GetConfigInt("contractorMaxDurability", 100));
            return;
        }

        if (id.Contains("info barter"))
        {
            SetRepairable(presetRoot, GetConfigInt("infoBarterDurability", 80), GetConfigInt("infoBarterMaxDurability", 100));
            return;
        }

        if (id.Contains("btc barter"))
        {
            SetRepairable(presetRoot, GetConfigInt("BTCBarterDurability", 100), GetConfigInt("BTCBarterMaxDurability", 100));
            return;
        }

        if (id.Contains("edition"))
        {
            SetRepairable(presetRoot, GetConfigInt("bossWeaponDurability", 90), GetConfigInt("bossWeaponMaxDurability", 100));
        }
    }

    private void SetRepairable(JsonObject presetRoot, int durability, int maxDurability)
    {
        var upd = presetRoot["upd"] as JsonObject ?? new JsonObject();
        presetRoot["upd"] = upd;
        upd["Repairable"] = new JsonObject
        {
            ["Durability"] = durability,
            ["MaxDurability"] = maxDurability
        };
    }

    private PresetData RegeneratePresetIds(PresetData preset)
    {
        var idMap = new Dictionary<string, string>();
        foreach (var item in preset.Items)
        {
            var oldId = item["_id"]?.ToString();
            if (string.IsNullOrWhiteSpace(oldId))
            {
                continue;
            }

            idMap[oldId] = GenerateId();
        }

        foreach (var item in preset.Items)
        {
            var oldId = item["_id"]?.ToString();
            if (!string.IsNullOrWhiteSpace(oldId) && idMap.TryGetValue(oldId, out var newId))
            {
                item["_id"] = newId;
            }

            var parentId = item["parentId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(parentId) && idMap.TryGetValue(parentId, out var newParent))
            {
                item["parentId"] = newParent;
            }
        }

        if (idMap.TryGetValue(preset.RootId, out var newRoot))
        {
            preset.RootId = newRoot;
        }

        return preset;
    }

    private int GetRandomIndex(int min, int max)
    {
        if (max < min)
        {
            (min, max) = (max, min);
        }

        return Constants.GetRandomUtil().GetInt(min, max, false);
    }

    private string GenerateId()
    {
        return Helper.GenerateMongoId().ToString();
    }

    private void SetPresetLocale(string presetKey, string presetId, JsonObject? rootItem)
    {
        var displayName = GetPresetDisplayName(presetId, rootItem);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        _localeManager.SetENLocale(presetKey, displayName);
        _localeManager.SetENServerLocale(presetKey, displayName);
    }

    private string GetPresetDisplayName(string presetId, JsonObject? rootItem)
    {
        var rawName = presetId.Replace('_', ' ').Trim();
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return rawName;
        }

        var tpl = rootItem?["_tpl"]?.ToString();
        if (string.IsNullOrWhiteSpace(tpl))
        {
            return rawName;
        }

        var baseShortName = _localeManager.GetENLocale($"{tpl} ShortName");
        if (string.IsNullOrWhiteSpace(baseShortName) ||
            baseShortName.StartsWith("UNKNOWN LOCALE ID", StringComparison.OrdinalIgnoreCase))
        {
            return rawName;
        }

        var normalizedRaw = NormalizeName(rawName);
        var normalizedBase = NormalizeName(baseShortName);
        if (string.IsNullOrWhiteSpace(normalizedBase) || !normalizedRaw.StartsWith(normalizedBase, StringComparison.Ordinal))
        {
            return rawName;
        }

        var prefixLength = GetNormalizedPrefixLength(rawName, normalizedBase);
        if (prefixLength <= 0 || prefixLength >= rawName.Length)
        {
            return rawName;
        }

        var suffix = rawName[prefixLength..].Trim();
        return string.IsNullOrWhiteSpace(suffix) ? rawName : suffix;
    }

    private static string NormalizeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static int GetNormalizedPrefixLength(string source, string normalizedPrefix)
    {
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            return 0;
        }

        var matched = 0;
        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];
            if (!char.IsLetterOrDigit(ch))
            {
                continue;
            }

            if (char.ToLowerInvariant(ch) != normalizedPrefix[matched])
            {
                return 0;
            }

            matched++;
            if (matched == normalizedPrefix.Length)
            {
                return i + 1;
            }
        }

        return 0;
    }

    private string GetPresetKey(string presetId, JsonObject? rootItem)
    {
        if (_presetIdMap.TryGetValue(presetId, out var existing))
        {
            return existing;
        }

        var deterministic = GetDeterministicPresetId(presetId, rootItem);
        _presetIdMap[presetId] = deterministic;

        if (rootItem != null)
        {
            var upd = rootItem["upd"] as JsonObject ?? new JsonObject();
            rootItem["upd"] = upd;
            upd["sptPresetId"] = deterministic;
        }

        return deterministic;
    }

    private static string? TryGetPresetId(JsonObject? rootItem)
    {
        if (rootItem?["upd"] is not JsonObject upd)
        {
            return null;
        }

        return upd["sptPresetId"]?.ToString();
    }

    private static string GetDeterministicPresetId(string presetId, JsonObject? rootItem)
    {
        var tpl = rootItem?["_tpl"]?.ToString() ?? string.Empty;
        var seed = string.IsNullOrWhiteSpace(tpl) ? presetId : $"{presetId}:{tpl}";
        return Helper.GenerateSha256Id(seed.ToLowerInvariant());
    }

    private static string ReplaceFirstUnderscore(string value)
    {
        var index = value.IndexOf('_');
        if (index < 0)
        {
            return value;
        }

        return value.Remove(index, 1).Insert(index, " ");
    }

    private static bool IsBasicPreset(string presetId)
    {
        return presetId.Contains("basic", StringComparison.OrdinalIgnoreCase);
    }

    private int GetConfigInt(string key, int defaultValue)
    {
        if (Config is not JsonObject obj)
        {
            return defaultValue;
        }

        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
        {
            return defaultValue;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var intVal))
            {
                return intVal;
            }

            if (value.TryGetValue<double>(out var doubleVal))
            {
                return (int)doubleVal;
            }
        }

        return defaultValue;
    }

    public sealed class PresetData
    {
        public string RootId { get; set; }
        public List<JsonObject> Items { get; }

        public PresetData(string rootId, List<JsonObject> items)
        {
            RootId = rootId;
            Items = items;
        }
    }
}
