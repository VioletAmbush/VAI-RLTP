import { AbstractModManager } from "./AbstractModManager"
import { Constants } from "./Constants"


export class ContainersManager extends AbstractModManager
{
    protected configName: string = "ContainersConfig"

    protected afterPostDB(): void
    {
        for (let itemConfig of this.config.items)
        {
            const item = this.databaseTables.templates.items[itemConfig.id]

            if (!item._props.Grids || item._props.Grids.length == 0)
            {
                this.logger.error(`${Constants.ModTitle}: Could not find Grid on item ${item._id}`)
                continue
            }
            
            const grid = item._props.Grids[0]

            grid._props.cellsV = itemConfig.height
            grid._props.cellsH = itemConfig.width
        }

        this.logger.info(`${Constants.ModTitle}: Containers changes applied!`)
    }
}