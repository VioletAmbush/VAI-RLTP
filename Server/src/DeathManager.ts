import { ProfileHelper } from "@spt/helpers/ProfileHelper"
import { IPmcData } from "@spt/models/eft/common/IPmcData"
import { IItem } from "@spt/models/eft/common/tables/IItem"
import { ILogger } from "@spt/models/spt/utils/ILogger"
import { SaveServer } from "@spt/servers/SaveServer"
import { StaticRouterModService } from "@spt/services/mod/staticRouter/StaticRouterModService"
import { DependencyContainer } from "tsyringe"
import { AbstractModManager } from "./AbstractModManager"
import { Constants } from "./Constants"
import { HealingManager } from "./HealingManager"
import { Helper } from "./Helper"
import { StartManager as StartManager } from "./StartManager"
import { WipeManager } from "./WipeManager"
import { ExitStatus } from "@spt/models/enums/ExitStatis"

export class DeathManager extends AbstractModManager
{
    protected configName: string = "DeathConfig"

    private wipeManager : WipeManager
    private healingManager: HealingManager
    private startManager : StartManager
    private staticRouter: StaticRouterModService

    constructor (wipeManager: WipeManager, healingManager: HealingManager, startManager: StartManager)
    {
        super()

        this.wipeManager = wipeManager
        this.healingManager = healingManager
        this.startManager = startManager
    }

    protected preSptInitialize(container: DependencyContainer): void
    {
        super.preSptInitialize(container)

        this.staticRouter = container.resolve<StaticRouterModService>("StaticRouterModService");
        this.logger = container.resolve<ILogger>("WinstonLogger")
    }

    protected afterPreSpt(): void
    {
		this.staticRouter.registerStaticRouter(`${Constants.ModTitle}: Raid end static route`,
        [
            {
                url: "/client/match/local/end",
			    action: async (url, info, sessionId, output) =>
                {
                    try 
                    {
                        if (!info.results.profile.Info.GameVersion || 
                            !(info.results.profile.Info.GameVersion as string).startsWith("VAI Rogue-lite"))
                        {
                            return output;
                        }

                        if (info.results.result != ExitStatus.SURVIVED &&
                            info.results.result != ExitStatus.RUNNER &&
                            info.results.result != ExitStatus.TRANSIT)
                        {
                            this.wipeManager.onPlayerDied(sessionId)
                            this.healingManager.onPlayerDied(sessionId)
                        }

                        this.startManager.onPlayerExtracted(info, sessionId)

                        Constants.Container.resolve<SaveServer>("SaveServer").saveProfile(sessionId)
                    }
                    catch(e)
                    {
                        Constants.getLogger().error(`${Constants.ModTitle}: ${url} route error: \n${e}`)
                    }

                    return output;
			    }
            }
        ], "spt");

        this.logger.info(`${Constants.ModTitle}: Raid end router set!`)

        // Used in development for preset configuration
        if (Constants.PrintPresetsOnFleaEnter)
        {
            this.staticRouter.registerStaticRouter(`${Constants.ModTitle}: Ragfair enter static route`,
            [
                {
                    url: "/client/ragfair/find",
                    action: async (url, info, sessionId, output) =>
                    {
                        const profileHelper = Constants.Container.resolve<ProfileHelper>("ProfileHelper")
                        const profile: IPmcData = profileHelper.getPmcProfile(sessionId)

                        this.printSlot(profile, "FirstPrimaryWeapon")
                        this.printSlot(profile, "SecondPrimaryWeapon")
                        this.printSlot(profile, "Holster")
                        
                        // Print all children of all THICC cases for presets config
                        this.printChildren(profile, "5b6d9ce188a4501afc1b2b25")
                        this.printChildren(profile, "5c0a840b86f7742ffa4f2482")

                        return output;
                    }
                }
            ], "spt");

            this.logger.info(`${Constants.ModTitle}: Raid end debug router set!`)
        }
    }

    private printSlot(profile: IPmcData, slot: string)
    {
        const root: IItem = profile.Inventory.items.find(item => item.slotId == slot)

        if (!root)
        {
            return
        }

        console.log(`"": ${this.jsonUtil.serialize(Helper.getItemTree(profile.Inventory.items, root))},`)
    }

    private printChildren(profile: IPmcData, parentTemplateId: string)
    {
        const containers: IItem[] = profile.Inventory.items
            .filter(item => item._tpl == parentTemplateId)

        let i = 0

        for (let container of containers)
        {
            const roots = profile.Inventory.items.filter(item => item.parentId == container._id)

            for (let root of roots)
            {
                console.log(`"Container_${i}": ${this.jsonUtil.serialize(Helper.getItemTree(profile.Inventory.items, root))},`)
            }

            i++
        }
    }
}