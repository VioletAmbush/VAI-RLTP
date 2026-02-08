using System.Linq;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class StartManager(
    HideoutManager hideoutManager,
    ProfilesManager profilesManager) : AbstractModManager
{
    private readonly HideoutManager _hideoutManager = hideoutManager;
    private readonly ProfilesManager _profilesManager = profilesManager;
    private bool _anyItemGiven;

    protected override string ConfigName => "StartConfig";

    public void OnPlayerExtracted(JsonObject results, MongoId sessionId)
    {
        if (!IsEnabled())
        {
            return;
        }

        _anyItemGiven = false;

        FillStash(results, sessionId);

        if (_anyItemGiven)
        {
            Constants.GetLogger().Info($"{Constants.ModTitle}: Starting items given!");
        }
    }

    private void FillStash(JsonObject results, MongoId sessionId)
    {
        var profile = ModContext.Current.ProfileHelper.GetPmcProfile(sessionId);
        if (profile is null)
        {
            return;
        }

        var inventory = profile.Inventory;
        if (inventory?.Items is null)
        {
            return;
        }

        var databaseTables = ModContext.Current.DatabaseServer.GetTables();
        var items = inventory.Items;
        var stashId = inventory.Stash?.ToString() ?? string.Empty;

        var result = results["result"]?.ToString();
        var bonusTarget = result != null
            && (string.Equals(result, "SURVIVED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result, "RUNNER", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result, "TRANSIT", StringComparison.OrdinalIgnoreCase))
            ? "survive"
            : "death";

        if (profile.Hideout?.Areas != null)
        {
            foreach (var area in profile.Hideout.Areas)
            {
                var type = ((int)area.Type).ToString();
                var level = area.Level?.ToString() ?? string.Empty;

                var bonuses = _hideoutManager.GetAreaBonus(type, level, bonusTarget, ModContext.Current.RandomUtil);
                AddAreaBonuses(items, stashId, bonuses);
            }
        }

        var profileNode = results["profile"] as JsonObject ?? results["Profile"] as JsonObject;
        var infoNode = profileNode?["Info"] as JsonObject ?? profileNode?["info"] as JsonObject;
        var gameVersion = infoNode?["GameVersion"]?.ToString() ?? infoNode?["gameVersion"]?.ToString() ?? string.Empty;
        var profileBonuses = _profilesManager.GetProfileBonus(gameVersion, bonusTarget, stashId);
        if (profileBonuses.Count > 0)
        {
            items.AddRange(profileBonuses);
            _anyItemGiven = true;
        }

        TryAddSurviveRubles(items, stashId, bonusTarget);

        Helper.FillLocations(profile, databaseTables);
    }

    private void AddAreaBonuses(List<Item> items, string stashId, List<AreaBonus> bonuses)
    {
        if (bonuses.Count < 1)
        {
            return;
        }

        foreach (var bonus in bonuses)
        {
            if (string.IsNullOrWhiteSpace(bonus.TemplateId))
            {
                continue;
            }

            var amount = GetRandomInt(bonus.AmountMin, bonus.AmountMax, ModContext.Current.RandomUtil);
            if (amount == 0)
            {
                continue;
            }

            if (amount == 1 || Helper.IsStackable(bonus.TemplateId))
            {
                var item = new Item
                {
                    Id = GenerateId(),
                    Template = new MongoId(bonus.TemplateId),
                    ParentId = stashId,
                    SlotId = "hideout"
                };

                if (amount > 1)
                {
                    item.Upd = new Upd
                    {
                        StackObjectsCount = amount
                    };
                }

                items.Add(item);
                _anyItemGiven = true;
            }
            else
            {
                for (var i = 0; i < amount; i++)
                {
                    var item = new Item
                    {
                        Id = GenerateId(),
                        Template = new MongoId(bonus.TemplateId),
                        ParentId = stashId,
                        SlotId = "hideout"
                    };

                    items.Add(item);
                    _anyItemGiven = true;
                }
            }
        }
    }

    private void TryAddSurviveRubles(List<Item> items, string stashId, string bonusTarget)
    {
        if (bonusTarget != "survive")
        {
            return;
        }

        var sum = 0;
        foreach (var item in items)
        {
            var tpl = item.Template.ToString();
            if (!string.Equals(tpl, "60b0f7057897d47c5b04ab94", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var count = GetStackCount(item.Upd);
            if (count > 0)
            {
                sum += count;
            }
        }

        if (sum <= 0)
        {
            return;
        }

        var rubles = new Item
        {
            Id = GenerateId(),
            Template = new MongoId("5449016a4bdc2d6f028b456f"),
            ParentId = stashId,
            SlotId = "hideout",
            Upd = new Upd
            {
                StackObjectsCount = sum * 2000
            }
        };

        items.Add(rubles);
        _anyItemGiven = true;
    }

    private static MongoId GenerateId()
    {
        return Helper.GenerateMongoId();
    }

    private static int GetStackCount(Upd? upd)
    {
        if (upd?.StackObjectsCount is null)
        {
            return 0;
        }

        try
        {
            var count = Convert.ToDouble(upd.StackObjectsCount);
            return (int)Math.Round(count, MidpointRounding.AwayFromZero);
        }
        catch
        {
            return 0;
        }
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
