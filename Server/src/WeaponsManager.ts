import { AbstractModManager } from "./AbstractModManager"
import { Constants } from "./Constants"


export class WeaponsManager extends AbstractModManager
{
    protected configName: string = "WeaponsConfig"

    protected afterPostDB(): void
    {
        if (this.config.unlootableWeapons != true)
        {
            return
        }

        for (let itemKey in this.databaseTables.templates.items)
        {
            let isLootable = false

            for (let lootableWeaponId of this.config.lootableWeapons)
            {
                if (itemKey == lootableWeaponId)
                {
                    isLootable = true
                    break
                }
            }

            if (isLootable)
                continue
            
            const item = this.databaseTables.templates.items[itemKey]

            if (item._props.RecoilForceBack && 
                item._props.RecoilForceUp &&
                item._props.RecoilCamera)
            {
                item._props.Unlootable = true

                // Hack to make weapons unlootable from any slot that contains letter 'o'
                item._props.UnlootableFromSlot = "o"
                item._props.UnlootableFromSide = [ "Bear", "Usec", "Savage" ]
            }
        }
        
        this.logger.info(`${Constants.ModTitle}: Weapons changes applied!`)

    }

    public getWeaponCategoryIds(categoryKey: string): { key: string, desc: string }[]
    {
        const category = this.config.categories[categoryKey]

        if (category)
        {
            const result: { key: string, desc: string }[] = []

            for (let key in category)
            {
                result.push({ key: key, desc: category[key] })
            }

            return result
        }

        return []
    }

    public getWeaponDescription(weaponId: string): string | null
    {
        for (let categoryKey in this.config.categories)
        {
            const item = this.config.categories[categoryKey][weaponId]

            if (item && item != "")
            {
                return item
            }
        }

        return weaponId
    }
}