using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using System.Text.Json.Nodes;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class BotsManager : AbstractModManager
{
    protected override string ConfigName => "BotsConfig";

    protected override void AfterPostDb()
    {
        var botsConfig = GetConfigObject("bots");
        if (botsConfig is null || botsConfig.Count == 0)
        {
            return;
        }

        var botTypes = DatabaseTables.Bots?.Types;
        if (botTypes is null)
        {
            return;
        }

        foreach (var (botKey, botNode) in botsConfig)
        {
            if (botNode is not JsonObject botConfig)
            {
                continue;
            }

            if (!botTypes.TryGetValue(botKey, out var bot))
            {
                continue;
            }

            if (botConfig["lootMultipliers"] is not JsonArray lootMultipliers || lootMultipliers.Count == 0)
            {
                continue;
            }

            var pools = bot.BotInventory?.Items;
            if (pools is null)
            {
                continue;
            }

            foreach (var lmNode in lootMultipliers.OfType<JsonObject>())
            {
                var templateId = lmNode["templateId"]?.ToString();
                var multiplier = GetNumberValue(lmNode["multiplier"]);
                if (string.IsNullOrWhiteSpace(templateId) || multiplier is null || !MongoId.IsValidMongoId(templateId))
                {
                    continue;
                }

                var templateMongo = new MongoId(templateId);
                foreach (var pool in EnumeratePools(pools))
                {
                    if (pool is null || !pool.TryGetValue(templateMongo, out var currentCount))
                    {
                        continue;
                    }

                    var newCount = Math.Max(1, (int)Math.Floor(currentCount * multiplier.Value));
                    pool[templateMongo] = newCount;
                }
            }
        }
    }

    private static IEnumerable<Dictionary<MongoId, double>?> EnumeratePools(ItemPools pools)
    {
        yield return pools.Backpack;
        yield return pools.Pockets;
        yield return pools.SpecialLoot;
        yield return pools.TacticalVest;
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
}
