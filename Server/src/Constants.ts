import { IDatabaseTables } from "@spt/models/spt/server/IDatabaseTables"
import { ILogger } from "@spt/models/spt/utils/ILogger"
import { HashUtil } from "@spt/utils/HashUtil"
import { JsonUtil } from "@spt/utils/JsonUtil"
import { RandomUtil } from "@spt/utils/RandomUtil"
import { DependencyContainer } from "tsyringe"
import { DatabaseServer } from "@spt/servers/DatabaseServer"


export class Constants 
{
    // Debug options
    public static MinimumPrices: boolean = false
    public static PrintPresetsOnFleaEnter: boolean = false
    public static PrintNewHashes: boolean = false
    public static NoNewQuestsStartRequirements: boolean = false
    public static NoQuestlockedItems: boolean = false
    public static IgnoreMockQuestlocks: boolean = false
    public static AllPresetsUnconditional: boolean = false
    public static EasyQuests: boolean = false
    public static PrintQuestCount: boolean = false
    public static PrintPresetCount: boolean = false
    public static InstantCrafting: boolean = false
    public static EasyCrafting: boolean = false

    public static ModTitle: string = "VAI-RLTP"

    public static Container: DependencyContainer

    public static getDatabaseTables(): IDatabaseTables
    {
        return this.Container.resolve<DatabaseServer>("DatabaseServer").getTables()
    }

    public static getJsonUtil(): JsonUtil
    {
        return this.Container.resolve<JsonUtil>("JsonUtil")
    }

    public static getHashUtil(): HashUtil
    {
        return this.Container.resolve<HashUtil>("HashUtil")
    }

    public static getLogger(): ILogger 
    {
        return this.Container.resolve<ILogger>("WinstonLogger")
    }

    public static getRandomUtil(): RandomUtil 
    {
        return this.Container.resolve<RandomUtil>("RandomUtil")
    }
}