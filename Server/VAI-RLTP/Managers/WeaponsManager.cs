using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class WeaponsManager : AbstractModManager
{
    protected override string ConfigName => "WeaponsConfig";

    protected override void AfterPostDb()
    {
        if (!GetConfigBool("unlootableWeapons"))
        {
            return;
        }

        var lootable = GetConfigArray("lootableWeapons")?.Select(node => node?.ToString()).Where(val => !string.IsNullOrWhiteSpace(val)).ToHashSet() ?? [];

        var items = DatabaseTables.Templates.Items;

        foreach (var entry in items)
        {
            var itemId = entry.Key.ToString();
            var item = entry.Value;
            if (string.IsNullOrWhiteSpace(itemId) || lootable.Contains(itemId))
            {
                continue;
            }

            var props = item.Properties;
            if (props is null)
            {
                continue;
            }

            if (HasPositiveValue(props.RecoilForceBack) &&
                HasPositiveValue(props.RecoilForceUp) &&
                HasPositiveValue(props.RecoilCamera))
            {
                props.Unlootable = true;
                props.UnlootableFromSlot = "o";
                props.UnlootableFromSide = new List<PlayerSideMask>
                {
                    PlayerSideMask.Bear,
                    PlayerSideMask.Usec,
                    PlayerSideMask.Savage
                };
            }
        }

        Constants.GetLogger().Info($"{Constants.ModTitle}: Weapons changes applied!");
    }

    private static bool HasPositiveValue(double? value)
    {
        return value is not null && value.Value > 0;
    }

    public List<(string Key, string Desc)> GetWeaponCategoryIds(string categoryKey)
    {
        var categories = GetConfigObject("categories");
        if (categories is null || !categories.TryGetPropertyValue(categoryKey, out var categoryNode) || categoryNode is not JsonObject category)
        {
            return [];
        }

        return category.Select(kvp => (kvp.Key, kvp.Value?.ToString() ?? string.Empty)).ToList();
    }

    public string GetWeaponDescription(string weaponId)
    {
        var categories = GetConfigObject("categories");
        if (categories is null)
        {
            return weaponId;
        }

        foreach (var entry in categories)
        {
            var categoryNode = entry.Value;
            if (categoryNode is not JsonObject category)
            {
                continue;
            }

            if (category.TryGetPropertyValue(weaponId, out var descNode))
            {
                var desc = descNode?.ToString();
                if (!string.IsNullOrWhiteSpace(desc))
                {
                    return desc;
                }
            }
        }

        return weaponId;
    }
}
