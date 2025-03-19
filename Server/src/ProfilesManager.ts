import { IBotHideoutArea } from "@spt/models/eft/common/tables/IBotBase"
import { IItem } from "@spt/models/eft/common/tables/IItem"
import { IProfileSides, ITemplateSide, IProfileTraderTemplate } from "@spt/models/eft/common/tables/IProfileTemplate"
import { ItemBaseClassService } from "@spt/services/ItemBaseClassService"
import { HashUtil } from "@spt/utils/HashUtil"
import { DependencyContainer } from "tsyringe"
import { AbstractModManager } from "./AbstractModManager"
import { Constants } from "./Constants"
import { Helper } from "./Helper"
import { PresetsManager } from "./PresetsManager"
import { WipeManager } from "./WipeManager"
import { QuestStatus } from "@spt/models/enums/QuestStatus"
import { IQuestStatus } from "@spt/models/eft/common/tables/IBotBase"
import { StaticRouterModService } from "@spt/services/mod/staticRouter/StaticRouterModService"
import { ProfileHelper } from "@spt/helpers/ProfileHelper"
import { IPmcData } from "@spt/models/eft/common/IPmcData"
import { SaveServer } from "@spt/servers/SaveServer"
import { DatabaseServer } from "@spt/servers/DatabaseServer"
import { RecursiveCloner } from "@spt/utils/cloners/RecursiveCloner"
import { LocaleManager } from "./LocaleManager"

export class ProfilesManager extends AbstractModManager
{
    protected configName: string = "ProfilesConfig"

    private wipeManager: WipeManager
    private presetsManager: PresetsManager
    private localeManager: LocaleManager

    private hashUtil: HashUtil
    private staticRouter: StaticRouterModService
    private recursiveCloner: RecursiveCloner

    public priority: number = 3
    
    constructor(
        wipeManager: WipeManager,
        presetsManager: PresetsManager,
        localeManager: LocaleManager)
    {
        super()

        this.wipeManager = wipeManager
        this.presetsManager = presetsManager
        this.localeManager = localeManager
    }

    protected preSptInitialize(container: DependencyContainer): void
    {
        super.preSptInitialize(container)

        this.staticRouter = container.resolve<StaticRouterModService>("StaticRouterModService");
    }

    protected postDBInitialize(container: DependencyContainer): void
    {
        super.postDBInitialize(container)

        this.hashUtil = container.resolve<HashUtil>("HashUtil")
        this.recursiveCloner = container.resolve<RecursiveCloner>("RecursiveCloner")
    }

    protected afterPreSpt(): void 
    {
        const url = "/client/profile/status"

        this.staticRouter.registerStaticRouter(`${Constants.ModTitle}: Client launch static route`,
        [
            {
                url: url,
			    action: async (url, info, sessionId, output) =>
                {
                    try 
                    {
                        const profile = Constants.Container.resolve<ProfileHelper>("ProfileHelper").getPmcProfile(sessionId)

                        if (!profile.Info.GameVersion || !(profile.Info.GameVersion as string).startsWith("VAI Rogue-lite"))
                        {
                            return output;
                        }

                        this.trySetQuests(sessionId, profile)
                    }
                    catch(e)
                    {
                        Constants.getLogger().error(`${Constants.ModTitle}: ${url} route error: \n${e}`)
                    }

                    return output;
			    }
            }
        ], "spt");
    }

    protected afterPostDB(): void
    {
        for (let profileKey in this.config.profiles)
        {
            const profileConfig = this.config.profiles[profileKey]

            const copy = this.databaseTables.templates.profiles[profileConfig.copySource]

            if (copy)
            {
                this.addProfile(profileKey, profileConfig, copy)
            }
        }
        
        this.logger.info(`${Constants.ModTitle}: New profiles added!`)
    }

    public getProfileBonus(profileKey: string, target: "death" | "survive", parentId: string): IItem[]
    {
        const hashUtil: HashUtil = Constants.getHashUtil()

        const profileConfig = this.config.profiles[profileKey]

        let itemConfigs = []

        if (target == "death" && profileConfig.deathItems)
            itemConfigs = profileConfig.deathItems

        if (target == "survive" && profileConfig.surviveItems)
            itemConfigs = profileConfig.surviveItems

        const result: IItem[] = []

        if (itemConfigs && itemConfigs.length > 0)
        {
            for (let itemConfig of itemConfigs)
            {
                if (itemConfig.templateId && itemConfig.templateId != "")
                {
                    if (Helper.isStackable(itemConfig.templateId))
                    {
                        result.push({
                            _id: hashUtil.generate(),
                            _tpl: itemConfig.templateId,
                            parentId: parentId,
                            slotId: "hideout",
                            upd:  {
                                StackObjectsCount: itemConfig.count ? itemConfig.count : 1
                            }
                        })
                    }
                    else
                    {
                        const count = itemConfig.count ? itemConfig.count : 1

                        for (let i = 0; i < count; i++)
                        {
                            result.push({
                                _id: hashUtil.generate(),
                                _tpl: itemConfig.templateId,
                                parentId: parentId,
                                slotId: "hideout",
                            })
                        }
                    }
                }
                else if (itemConfig.presetId && itemConfig.presetId != "")
                {
                    const preset = this.presetsManager.resolvePreset(itemConfig.presetId, parentId)

                    const rootItem = preset.items.find(i => i._id == preset.rootId)

                    if (!rootItem)
                    {
                        continue
                    }

                    if (rootItem.upd)
                    {
                        rootItem.upd.StackObjectsCount = 1
                    }

                    result.push(...preset.items)
                }
            }
        }

        if (target == "death" && profileConfig.gungame && profileConfig.gungame.enabled == true)
        {
            const preset = this.presetsManager.resolveRandomPreset(
                profileConfig.gungame.presetCategories, 
                profileConfig.gungame.excludePresets, 
                profileConfig.gungame.includePresets, 
                parentId)

            const rootItem = preset.items.find(i => i._id == preset.rootId)

            if (rootItem)
            {
                if (rootItem.upd)
                {
                    rootItem.upd.StackObjectsCount = 1
                }

                result.push(...preset.items)
            }
        }

        return result
    }

    private addProfile(profileName: string, config: any, copySource: IProfileSides): void
    {
        var profile = this.recursiveCloner.clone(copySource)

        this.setSide(profileName, config, profile.bear)
        this.setSide(profileName, config, profile.usec)
        
        profile.descriptionLocaleKey = `${config.description}\n\n${config.actualDescription}`

        // Doesn't want to work
        // const profileDescriptionLocaleId = `launcher-profile-${profileName.replaceAll(" ", "").replaceAll(":", "").replaceAll("(", "").replaceAll(")", "").toLowerCase()}`

        // profile.descriptionLocaleKey = profileDescriptionLocaleId

        // this.localeManager.setENServerLocale(profile.descriptionLocaleKey, `${config.description} ${config.actualDescription}`)    

        this.databaseTables.templates.profiles[profileName] = profile
    }

    private setSide(profileName: string, config: any, side: ITemplateSide)
    {
        this.wipeManager.clearProfileStash(side.character, this.hashUtil)

        side.character.Inventory.items = side.character.Inventory.items
            .filter(item => item.parentId != side.character.Inventory.stash)

        if (config.clearAllItems == true)
        {
            side.character.Inventory.items = side.character.Inventory.items
                .filter(item => 
                    item.slotId != "Scabbard" && 
                    item.slotId != "SecuredContainer" && 
                    item.parentId != side.character.Inventory.stash)
        }

        side.character.Info.GameVersion = profileName

        side.trader = {
            initialLoyaltyLevel: Helper.getTradersRecords(0),
            initialStanding: {
                default: config.tradersStanding
            },
            initialSalesSum: 0,
            jaegerUnlocked: config.jaegerUnlocked ? config.jaegerUnlocked : false,
        }

        for (let areaKey in config.areas)
        {
            const areaConfig = config.areas[areaKey]
            const area: IBotHideoutArea = side.character.Hideout.Areas.find(area => area.type.toString() == areaKey)

            if (!area)
            {
                continue
            }

            area.level = areaConfig.startingLevel
        }

        for (let itemConfig of config.items)
        {
            if (itemConfig.templateId && itemConfig.templateId != "")
            {
                if (!itemConfig.count || itemConfig.count == 1 || Helper.isStackable(itemConfig.templateId))
                {
                    const item: IItem = {
                        _id: itemConfig.id ? itemConfig.id : this.hashUtil.generate(),
                        _tpl: itemConfig.templateId,
                        parentId: itemConfig.parentId ? itemConfig.parentId : side.character.Inventory.stash,
                        slotId: itemConfig.slotId ? itemConfig.slotId : "hideout"
                    } 

                    if (itemConfig.count && itemConfig.count > 1)
                    {
                        item.upd = {
                            StackObjectsCount: itemConfig.count ? itemConfig.count : 1
                        }
                    }
                    
                    side.character.Inventory.items.push(item)
                }
                else
                {
                    for (let i = 0; i < itemConfig.count; i++)
                    {
                        const item: IItem = {
                            _id: this.hashUtil.generate(),
                            _tpl: itemConfig.templateId,
                            parentId: itemConfig.parentId ? itemConfig.parentId : side.character.Inventory.stash,
                            slotId: itemConfig.slotId ? itemConfig.slotId : "hideout",
                            upd: {
                                StackObjectsCount: 1
                            }
                        }
                        
                        side.character.Inventory.items.push(item)
                    }
                }  
            }
            
            if (itemConfig.presetId && itemConfig.presetId != "")
            {
                const preset = this.presetsManager.resolvePreset(itemConfig.presetId, side.character.Inventory.stash)

                const rootItem = preset.items.find(i => i._id == preset.rootId)

                if (rootItem)
                {
                    if (rootItem.upd)
                    {
                        rootItem.upd.StackObjectsCount = 1
                    }

                    side.character.Inventory.items.push(...preset.items)
                }
            }
        }

        for (let skillKey in config.skills)
        {
            const skill = side.character.Skills.Common.find(skill => skill.Id == skillKey)

            if (!skill)
            {
                continue
            }

            const skillConfig = config.skills[skillKey]

            skill.Progress = skillConfig.startingLevel
        }

        side.character.Skills.Mastering = []
        side.character.Skills.Points = 0

        Helper.fillLocations(side.character, this.databaseTables)
    }

    private trySetQuests(sessionId: string, profile: IPmcData)
    {
        if (profile.Quests.length > 0)
        {
            return
        }

        const profileConfig = this.config.profiles[profile.Info.GameVersion]   

        if (!profileConfig || !profileConfig.completedQuests || profileConfig.completedQuests.length == 0)
        {
            return
        }

        const databaseTables = Constants.Container.resolve<DatabaseServer>("DatabaseServer").getTables()

        const all: boolean = profileConfig.completedQuests.find(questId => questId == "__all__")
        const questKeys = all ? 
            Object.keys(databaseTables.templates.quests) :
            profileConfig.completedQuests

        const quests: IQuestStatus[] = []

        for (let questKey of questKeys)
        {
            const quest = databaseTables.templates.quests[questKey]

            if (!quest)
            {
                Constants.getLogger().error(`${Constants.ModTitle}: Could not find quest with id ${questKey}`)

                continue
            }

            quests.push({
                qid: quest._id,
                startTime: 0,
                status: QuestStatus.AvailableForFinish,
                statusTimers: {},
                completedConditions: quest.conditions.AvailableForFinish.map(c => c.id),
                availableAfter: 0
            })
        }
        
        if (quests && quests.length > 0)
        {
            profile.Quests.push(...quests)
        }

        Constants.Container.resolve<SaveServer>("SaveServer").saveProfile(sessionId)

        Constants.getLogger().info(`${Constants.ModTitle}: Quests set for profile ${profile.Info.Nickname}`)
    }
}