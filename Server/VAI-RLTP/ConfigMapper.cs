using System.Text.Json.Nodes;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace VAI.RLTP;

public static class ConfigMapper
{
    public static List<Buff> MapBuffs(JsonNode? config)
    {
        var buffs = new List<Buff>();

        if (config is not JsonObject obj)
        {
            return buffs;
        }

        foreach (var (_, node) in obj)
        {
            if (node is not JsonObject buffConfig)
            {
                continue;
            }

            buffs.Add(MapBuff(buffConfig));
        }

        return buffs;
    }

    public static Buff MapBuff(JsonObject config)
    {
        return new Buff
        {
            BuffType = config["buffType"]?.GetValue<string>() ?? string.Empty,
            Chance = config["chance"]?.GetValue<double>() ?? 1,
            Delay = config["delay"]?.GetValue<double>() ?? 1,
            Duration = config["duration"]?.GetValue<double>() ?? 60,
            Value = config["value"]?.GetValue<double>() ?? 0,
            AbsoluteValue = config["absoluteValue"]?.GetValue<bool>() ?? true,
            SkillName = config["skillName"]?.GetValue<string>() ?? string.Empty
        };
    }

    public static string MapRarity(string rarity)
    {
        return rarity switch
        {
            "1" or "c" or "common" => "grey",
            "2" or "uc" or "uncommon" => "green",
            "3" or "e" or "epic" => "violet",
            "4" or "r" or "rare" => "yellow",
            "5" or "l" or "legendary" => "red",
            "6" or "u" or "unique" => "orange",
            "7" or "q" or "quest" => "blue",
            _ => "grey"
        };
    }
}
