using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Enums.Hideout;
using SPTarkov.Server.Core.Utils;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class HideoutManager : AbstractModManager
{
    protected override string ConfigName => "HideoutConfig";

    protected override void AfterPostDb()
    {
        var hideout = DatabaseTables.Hideout;

        if (GetConfigBool("removeGeneratorStageOneSecurityRequirement"))
        {
            ClearStageSecurityRequirement(hideout, HideoutAreas.Generator, HideoutAreas.Security);
        }

        if (GetConfigBool("removeWaterCollectorStageOneSecurityRequirement"))
        {
            ClearStageSecurityRequirement(hideout, HideoutAreas.WaterCollector, HideoutAreas.Security);
        }

        var areaTypes = GetConfigObject("areaTypes");
        if (areaTypes is null)
        {
            return;
        }

        foreach (var (areaKey, areaNode) in areaTypes)
        {
            if (areaNode is not JsonObject areaConfig)
            {
                continue;
            }

            if (!int.TryParse(areaKey, out var areaTypeValue))
            {
                continue;
            }

            var area = hideout.Areas.FirstOrDefault(a => a.Type.HasValue && (int)a.Type.Value == areaTypeValue);
            if (area is null)
            {
                continue;
            }

            if (IsConfigTrue(areaConfig, "clearCrafts") && area.Type.HasValue)
            {
                ClearCraftsForArea(hideout, area.Type.Value);
            }

            ApplyStageItemRequirements(area, areaConfig);
            ApplyStageAreaRequirements(area, areaConfig);
            ApplyStageCrafts(hideout, area, areaConfig);
        }

        Constants.GetLogger().Info($"{Constants.ModTitle}: Hideout changes applied!");
    }

    public List<AreaBonus> GetAreaBonus(string type, string stage, string target, RandomUtil randomUtil)
    {
        var areaTypes = GetConfigObject("areaTypes");
        if (areaTypes is null)
        {
            return [];
        }

        if (!areaTypes.TryGetPropertyValue(type, out var areaNode) || areaNode is not JsonObject areaConfig)
        {
            return [];
        }

        var stageKey = target == "death" ? "stageDeathBonuses" : "stageSurviveBonuses";
        if (!areaConfig.TryGetPropertyValue(stageKey, out var stageNode) || stageNode is not JsonObject stageBonuses)
        {
            return [];
        }

        if (!stageBonuses.TryGetPropertyValue(stage, out var bonusNode) || bonusNode is not JsonArray bonusArrays)
        {
            return [];
        }

        if (bonusArrays.Count == 0)
        {
            return [];
        }

        var index = GetRandomInt(0, bonusArrays.Count - 1, randomUtil);
        if (bonusArrays[index] is not JsonArray selected)
        {
            return [];
        }

        var result = new List<AreaBonus>();
        foreach (var bonus in selected.OfType<JsonObject>())
        {
            var templateId = bonus["templateId"]?.GetValue<string>() ?? string.Empty;
            var amountMin = bonus["amountMin"]?.GetValue<int>() ?? 0;
            var amountMax = bonus["amountMax"]?.GetValue<int>() ?? 0;
            result.Add(new AreaBonus(templateId, amountMin, amountMax));
        }

        return result;
    }

    private static void ClearStageSecurityRequirement(SPTarkov.Server.Core.Models.Spt.Hideout.Hideout hideout, HideoutAreas targetArea, HideoutAreas requiredArea)
    {
        var area = hideout.Areas.FirstOrDefault(a => a.Type == targetArea);
        if (area is null || area.Stages is null)
        {
            return;
        }

        if (!area.Stages.TryGetValue("1", out var stage) || stage?.Requirements is null)
        {
            return;
        }

        foreach (var req in stage.Requirements)
        {
            if (string.Equals(req.Type, "Area", StringComparison.OrdinalIgnoreCase) &&
                req.AreaType == (int)requiredArea)
            {
                req.RequiredLevel = 0;
            }
        }
    }

    private static void ClearCraftsForArea(SPTarkov.Server.Core.Models.Spt.Hideout.Hideout hideout, HideoutAreas areaType)
    {
        var recipes = hideout.Production?.Recipes;
        if (recipes is null)
        {
            return;
        }

        recipes.RemoveAll(recipe => recipe.AreaType == areaType);
    }

    private static void ApplyStageItemRequirements(HideoutArea area, JsonObject areaConfig)
    {
        if (!areaConfig.TryGetPropertyValue("stageItemRequirements", out var node) || node is not JsonObject stageReqs)
        {
            return;
        }

        foreach (var (stageKey, stage) in area.Stages)
        {
            if (!stageReqs.TryGetPropertyValue(stageKey, out var stageConfigNode) || stageConfigNode is not JsonArray stageConfig)
            {
                continue;
            }

            stage.Requirements ??= new List<StageRequirement>();
            stage.Requirements.RemoveAll(req => string.Equals(req.Type, "Item", StringComparison.OrdinalIgnoreCase));

            foreach (var reqConfig in stageConfig.OfType<JsonObject>())
            {
                var templateId = reqConfig["templateId"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(templateId) || !MongoId.IsValidMongoId(templateId))
                {
                    continue;
                }

                stage.Requirements.Add(new StageRequirement
                {
                    TemplateId = new MongoId(templateId),
                    Count = reqConfig["count"]?.GetValue<int>() ?? 0,
                    IsFunctional = false,
                    IsEncoded = false,
                    Type = "Item"
                });
            }
        }
    }

    private static void ApplyStageAreaRequirements(HideoutArea area, JsonObject areaConfig)
    {
        if (!areaConfig.TryGetPropertyValue("stageAreaRequirements", out var node) || node is not JsonObject stageReqs)
        {
            return;
        }

        foreach (var (stageKey, stage) in area.Stages)
        {
            if (!stageReqs.TryGetPropertyValue(stageKey, out var stageConfigNode) || stageConfigNode is not JsonArray stageConfig)
            {
                continue;
            }

            stage.Requirements ??= new List<StageRequirement>();
            stage.Requirements.RemoveAll(req => string.Equals(req.Type, "Area", StringComparison.OrdinalIgnoreCase));

            foreach (var reqConfig in stageConfig.OfType<JsonObject>())
            {
                stage.Requirements.Add(new StageRequirement
                {
                    AreaType = reqConfig["type"]?.GetValue<int>() ?? 0,
                    RequiredLevel = reqConfig["level"]?.GetValue<int>() ?? 0,
                    Type = "Area",
                    IsEncoded = false,
                    TemplateId = MongoId.Empty()
                });
            }
        }
    }

    private static void ApplyStageCrafts(SPTarkov.Server.Core.Models.Spt.Hideout.Hideout hideout, HideoutArea area, JsonObject areaConfig)
    {
        if (!areaConfig.TryGetPropertyValue("stageCrafts", out var node) || node is not JsonObject stageCrafts)
        {
            return;
        }

        var recipes = hideout.Production?.Recipes;
        if (recipes is null)
        {
            return;
        }

        foreach (var (stageKey, stageNode) in stageCrafts)
        {
            if (stageNode is not JsonObject stageConfig)
            {
                continue;
            }

            foreach (var (_, craftNode) in stageConfig)
            {
                if (craftNode is not JsonObject craftConfig)
                {
                    continue;
                }

                var craftId = craftConfig["id"]?.GetValue<string>();
                var resultId = craftConfig["resultId"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(craftId) || !MongoId.IsValidMongoId(craftId))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(resultId) || !MongoId.IsValidMongoId(resultId))
                {
                    continue;
                }

                var craft = new HideoutProduction
                {
                    Id = new MongoId(craftId),
                    AreaType = area.Type,
                    ProductionTime = Constants.InstantCrafting ? 1 : craftConfig["time"]?.GetValue<int>() ?? 0,
                    EndProduct = new MongoId(resultId),
                    Count = craftConfig["resultCount"]?.GetValue<int>() ?? 0,
                    Requirements = new List<Requirement>(),
                    NeedFuelForAllProductionTime = false,
                    Locked = false,
                    Continuous = false,
                    ProductionLimitCount = 0,
                    IsEncoded = false,
                    IsCodeProduction = false
                };

                craft.Requirements.Add(new Requirement
                {
                    AreaType = area.Type.HasValue ? (int?)area.Type.Value : null,
                    RequiredLevel = int.TryParse(stageKey, out var stageVal) ? stageVal : 0,
                    Type = "Area"
                });

                if (Constants.EasyCrafting)
                {
                    craft.Requirements.Add(new Requirement
                    {
                        TemplateId = new MongoId("5449016a4bdc2d6f028b456f"),
                        Count = 1,
                        Type = "Item",
                        IsFunctional = false,
                        IsEncoded = false
                    });
                }
                else
                {
                    if (craftConfig.TryGetPropertyValue("requirements", out var reqNode) && reqNode is JsonArray reqConfigs)
                    {
                        foreach (var reqConfig in reqConfigs.OfType<JsonObject>())
                        {
                            var templateId = reqConfig["templateId"]?.GetValue<string>();
                            if (string.IsNullOrWhiteSpace(templateId) || !MongoId.IsValidMongoId(templateId))
                            {
                                continue;
                            }

                            var tool = reqConfig["tool"]?.GetValue<bool>() ?? false;
                            craft.Requirements.Add(new Requirement
                            {
                                TemplateId = new MongoId(templateId),
                                Count = reqConfig["count"]?.GetValue<int>() ?? 0,
                                Type = tool ? "Tool" : "Item",
                                IsFunctional = false,
                                IsEncoded = false
                            });
                        }
                    }
                }

                recipes.Add(craft);
            }
        }
    }

    private static bool IsConfigTrue(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<bool>(out var res) && res;
    }

    private static int GetRandomInt(int min, int max, RandomUtil randomUtil)
    {
        if (max < min)
        {
            (min, max) = (max, min);
        }

        return randomUtil.GetInt(min, max, false);
    }
}

public sealed record AreaBonus(string TemplateId, int AmountMin, int AmountMax);
