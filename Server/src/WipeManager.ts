import { ProfileHelper } from "@spt/helpers/ProfileHelper"
import { IPmcData } from "@spt/models/eft/common/IPmcData"
import { IItem } from "@spt/models/eft/common/tables/IItem"
import { DatabaseServer } from "@spt/servers/DatabaseServer"
import { HashUtil } from "@spt/utils/HashUtil"
import { AbstractModManager } from "./AbstractModManager"
import { Constants } from "./Constants"

export class WipeManager extends AbstractModManager
{
    protected configName: string = "WipeConfig"
    
    public onPlayerDied(sessionId: string): void
    {
        if (this.config.enabled != true)
        {
            return
        }

        this.clearStash(sessionId)
    }

    private clearStash(sessionId: string): void
    {
        const profile = Constants.Container.resolve<ProfileHelper>("ProfileHelper").getPmcProfile(sessionId)
        const hashUtil = Constants.Container.resolve<HashUtil>("HashUtil")
        
        this.clearProfileStash(profile, hashUtil)

        this.logger.info(`${Constants.ModTitle}: Stash wiped! C:`)
    }

    public clearProfileStash(profile: IPmcData, hashUtil: HashUtil)
    {
        const securedIds = this.getSecuredIds(profile)

        profile.Inventory.items = profile.Inventory.items
            .filter(item => this.filterItem(item, securedIds))

        const pockets = profile.Inventory.items.find(item => item.slotId == "Pockets")

        if (!pockets)
        {
            this.logger.warning(`${Constants.ModTitle}: Pockets missing, fixing...`)

            profile.Inventory.items.push({
                _id: hashUtil.generate(),
                _tpl: "627a4e6b255f7527fb05a0f6",
                parentId: profile.Inventory.equipment,
                slotId: "Pockets"
            })
        }
    }

    private filterItem(
        item: IItem, 
        securedIds: string[]): boolean
    {
        return securedIds.includes(item._id)
    }

    private getSecuredIds(profile: IPmcData): string[]
    {
        let securedIds: string[] = []

        const securedContainer = profile.Inventory.items.find(item => item.slotId == "SecuredContainer")
        
        if (securedContainer)
        {
            securedIds = this.getContainerIds(securedContainer._id, securedIds, profile)
        }

        profile.Inventory.items
            .filter(item => this.config.securedItems.includes(item._tpl))
            .forEach(item => securedIds = this.getContainerIds(item._id, securedIds, profile))

        profile.Inventory.items
            .filter(item => this.config.ignoredItems.includes(item._tpl))
            .forEach(item => 
            {
                if (!securedIds.includes(item._id))
                    securedIds.push(item._id)
            })

        securedIds.push(profile.Inventory.stash) 
        securedIds.push(profile.Inventory.equipment)
        securedIds.push(profile.Inventory.questRaidItems)
        securedIds.push(profile.Inventory.questStashItems)
        securedIds.push(profile.Inventory.sortingTable)
        securedIds.push(...Object.values(profile.Inventory.hideoutAreaStashes))

        const pockets = profile.Inventory.items.find(item => item.slotId == "Pockets")

        if (pockets)
        {
            securedIds.push(pockets._id)
        }

        profile.Inventory.items
            .filter(item => item.parentId == profile.Inventory.questRaidItems || item.parentId == profile.Inventory.questStashItems)
            .forEach(item => securedIds.push(item._id))

        const scabItem = profile.Inventory.items.find(item => item.slotId == "Scabbard")

        if (scabItem)
        {
            securedIds.push(scabItem._id)
        }

        return securedIds
    }

    private getContainerIds(containerId: string, ids: string[], profile: IPmcData)
    {
        if (!ids.includes(containerId))
        {
            ids.push(containerId)
        }

        for (let i = 0; i < 20; i++)
        {
            const count = ids.length

            ids.forEach(id => 
            {
                profile.Inventory.items
                    .filter(item => item.parentId == id)
                    .forEach(item => 
                {
                    if (!ids.includes(item._id))
                    {
                        ids.push(item._id)
                    }
                })
            })

            if (ids.length == count)
            {
                break
            }
        }

        return ids
    }
}