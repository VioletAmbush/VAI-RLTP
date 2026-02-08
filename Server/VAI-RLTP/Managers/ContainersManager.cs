using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class ContainersManager : AbstractModManager
{
    protected override string ConfigName => "ContainersConfig";

    protected override void AfterPostDb()
    {
        var itemsConfig = GetConfigArray("items");
        if (itemsConfig is null || itemsConfig.Count == 0)
        {
            return;
        }

        var items = DatabaseTables.Templates.Items;

        foreach (var entry in itemsConfig)
        {
            if (entry is not JsonObject itemConfig)
            {
                continue;
            }

            var id = itemConfig["id"]?.GetValue<string>();
            var height = itemConfig["height"]?.GetValue<int>() ?? 0;
            var width = itemConfig["width"]?.GetValue<int>() ?? 0;

            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!MongoId.IsValidMongoId(id))
            {
                Constants.GetLogger().Error($"{Constants.ModTitle}: Item with id {id} is not a valid MongoId!");
                continue;
            }

            if (!items.TryGetValue(new MongoId(id), out var item))
            {
                Constants.GetLogger().Error($"{Constants.ModTitle}: Item with id {id} not found!");
                continue;
            }

            var props = item.Properties;
            if (props is null)
            {
                Constants.GetLogger().Error($"{Constants.ModTitle}: Could not read properties on item {id}");
                continue;
            }

            var firstGrid = props.Grids?.FirstOrDefault();
            if (firstGrid is null)
            {
                Constants.GetLogger().Error($"{Constants.ModTitle}: Could not find Grid on item {id}");
                continue;
            }

            var gridProps = firstGrid.Properties;
            if (gridProps is null)
            {
                Constants.GetLogger().Error($"{Constants.ModTitle}: Could not find Grid properties on item {id}");
                continue;
            }

            gridProps.CellsV = height;
            gridProps.CellsH = width;
        }

        Constants.GetLogger().Info($"{Constants.ModTitle}: Containers changes applied!");
    }
}
