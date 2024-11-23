import { ProfileHelper } from "@spt/helpers/ProfileHelper"
import { IPmcData } from "@spt/models/eft/common/IPmcData"
import { Item } from "@spt/models/eft/common/tables/IItem"
import { AbstractModManager } from "./AbstractModManager"
import { Constants } from "./Constants"
import { Helper } from "./Helper"
import { HideoutManager, AreaBonus } from "./HideoutManager"
import { HashUtil } from "@spt/utils/HashUtil"
import { RandomUtil } from "@spt/utils/RandomUtil"
import { DatabaseServer } from "@spt/servers/DatabaseServer"
import { ProfilesManager } from "./ProfilesManager"


export class StartManager extends AbstractModManager
{
    protected configName: string = "StartConfig"

    private hideoutManager: HideoutManager
    private profilesManager: ProfilesManager

    private anyItemGiven: boolean = false

    constructor(hideoutManager: HideoutManager, profilesManager: ProfilesManager)
    {
        super()

        this.hideoutManager = hideoutManager
        this.profilesManager = profilesManager
    }

    public onPlayerExtracted(info: any, sessionId: string): void
    {
        if (this.config.enabled != true)
        {
            return
        }

        this.anyItemGiven = false

        this.fillStash(info, sessionId)

        if (this.anyItemGiven)
        {
            Constants.getLogger().info(`${Constants.ModTitle}: Starting items given!`)
        }
    }

    private fillStash(info, sessionId)
    {
        const profileHelper = Constants.Container.resolve<ProfileHelper>("ProfileHelper")
        const databaseTables = Constants.Container.resolve<DatabaseServer>("DatabaseServer").getTables()
        const hashUtil = Constants.Container.resolve<HashUtil>("HashUtil")
        const randomUtil = Constants.Container.resolve<RandomUtil>("RandomUtil")
        const profile = profileHelper.getPmcProfile(sessionId)

        const areas = profile.Hideout.Areas

        let bonusTarget: "death" | "survive" = (info.exit != "survived" && info.exit != "runner") ? "death" : "survive"

        for (let area of areas)
        {
            this.addItem(
                profile, 
                hashUtil, 
                randomUtil, 
                this.hideoutManager.getAreaBonus(
                    area.type.toString(), 
                    area.level.toString(), 
                    bonusTarget, 
                    randomUtil))
        }

        const profileBonuses: Item[] = this.profilesManager.getProfileBonus(info.profile.Info.GameVersion, bonusTarget, profile.Inventory.stash)

        if (profileBonuses.length > 0)
        {
            profile.Inventory.items.push(...profileBonuses)
            this.anyItemGiven = true
        }

        Helper.fillLocations(profile, databaseTables)
    }

    private addItem(
        profile: IPmcData, 
        hashUtil: HashUtil, 
        randomUtil: RandomUtil, 
        bonuses: AreaBonus[])
    {
        if (!bonuses || bonuses.length < 1)
        {
            return
        }

        for (let bonus of bonuses)
        {
            if (bonus.templateId == "")
            {
                continue
            }

            const item: Item = 
            {
                _id: hashUtil.generate(),
                _tpl: bonus.templateId,
                parentId: profile.Inventory.stash,
                slotId: "hideout"
            }

            const amount = randomUtil.getInt(bonus.amountMin, bonus.amountMax)

            if (amount != 0)
            {
                if (amount == 1 || Helper.isStackable(item._tpl))
                {
                    if (amount > 1)
                    {
                        item.upd = 
                        {
                            StackObjectsCount: amount
                        }
                    }

                    profile.Inventory.items.push(item)
                }
                else
                {
                    for (let i = 0; i < amount; i++)
                    {
                        const itemToAdd = 
                        {
                            _id: hashUtil.generate(),
                            _tpl: item._tpl,
                            parentId: item.parentId,
                            slotId: item.slotId
                        }

                        profile.Inventory.items.push(itemToAdd)
                    }
                }

                this.anyItemGiven = true
            }
        }
    }
}