import { IBarterScheme, ITrader } from "@spt/models/eft/common/tables/ITrader"
import { HashUtil } from "@spt/utils/HashUtil"
import { DependencyContainer } from "tsyringe"
import { AbstractModManager } from "./AbstractModManager"
import { Constants } from "./Constants"
import { PresetsManager } from "./PresetsManager"
import { TradeHelper } from "@spt/helpers/TradeHelper"
import { IPmcData } from "@spt/models/eft/common/IPmcData"
import { IProcessBuyTradeRequestData } from "@spt/models/eft/trade/IProcessBuyTradeRequestData"
import { Item, Upd } from "@spt/models/eft/common/tables/IItem"
import { IItemEventRouterResponse } from "@spt/models/eft/itemEvent/IItemEventRouterResponse"
import { ProfileHelper } from "@spt/helpers/ProfileHelper"
import { TProfileChanges, ProfileChange, Product } from "@spt/models/eft/itemEvent/IItemEventRouterBase"
import { IDatabaseTables } from "@spt/models/spt/server/IDatabaseTables"
import { Helper } from "./Helper"
import { DatabaseServer } from "@spt/servers/DatabaseServer"
import { QuestsManager } from "./QuestsManager"

export class TradersManager extends AbstractModManager
{
    protected configName: string = "TradersConfig"

    private presetsManager: PresetsManager
    private questsManager: QuestsManager
    private hashUtil: HashUtil

    private static buyItemOriginal: any

    private configs: any[] = []
    public priority: number = 2

    public flag: boolean = false

    constructor(presetsManager: PresetsManager, questsManager: QuestsManager)
    {
        super()

        this.presetsManager = presetsManager
        this.questsManager = questsManager
    }

    protected afterPreSpt(): void
    {
        for (let name of this.config.traderConfigNames)
        {
            const config = require(`../config/Traders/${name}.json`)

            if (config.enabled != true)
            {
                continue
            }

            this.configs.push(config) 
        }

        // this.container.afterResolution("TradeHelper", (_t, tradeHelper: TradeHelper) => 
        // {
        //     TradersManager.buyItemOriginal = tradeHelper.buyItem
    
        //     tradeHelper.buyItem = TradersManager.buyItem
        // }, {frequency: "Always"});
    }

    protected postDBInitialize(container: DependencyContainer): void
    {
        super.postDBInitialize(container)

        this.hashUtil = container.resolve<HashUtil>("HashUtil")
    }

    protected afterPostDB(): void
    {
        for (let config of this.configs)
        {
            this.setTrader(config)
        }

        this.logger.info(`${Constants.ModTitle}: Traders changes applied!`)
    }

    private setTrader(config: any)
    {
        const trader = this.databaseTables.traders[Helper.acronymToTraderId(config.traderAcr)]

        if (!trader)
        {
            return
        }

        if (config.clearAssort == true)
        {
            trader.assort.items = []
            trader.assort.barter_scheme = {}
            trader.assort.loyal_level_items = {}

            trader.questassort.started = {}
            trader.questassort.success = {}
            trader.questassort.fail = {}
        }

        if (config.disableSell == true)
        {
            trader.base.items_buy.category = []
            trader.base.items_buy.id_list = []
        }

        if (config.disableInsurance == true)
        {
            trader.base.insurance.availability = false
        }

        if (config.lockFromStart == true)
        {
            trader.base.unlockedByDefault = false
        }

        if (this.config.resetTradersTimers == true)
        {
            trader.base.nextResupply = 1631486713
        }

        if (Constants.AllPresetsUnconditional)
        {
            let presetIds = this.presetsManager.resolveAllPresetIds()

            presetIds.forEach(presetId => 
            {
                this.setTraderPreset(trader, config, { presetId: presetId, loyaltyLevel: 1 })
            })
        }
        
        if (config.items && config.items.length > 0)
        {
            config.items.forEach(item => 
            {
                if (!Constants.AllPresetsUnconditional && item.presetId && item.presetId !== "")
                {
                    this.setTraderPreset(trader, config, item)
                }
    
                if (item.itemTemplateId && item.itemTemplateId !== "")
                {
                    this.setTraderItem(trader, config, item)
                }
            })
        }

        if (config.categorizedItems)
        {
            for (let categoryKey in config.categorizedItems)
            {
                let items = config.categorizedItems[categoryKey]

                items.forEach(item => 
                {
                    if (!Constants.AllPresetsUnconditional && item.presetId && item.presetId !== "")
                    {
                        this.setTraderPreset(trader, config, item)
                    }
        
                    if (item.itemTemplateId && item.itemTemplateId !== "")
                    {
                        this.setTraderItem(trader, config, item)
                    }
                })
            }
        }
        
    }

    private setTraderPreset(trader: ITrader, traderConfig: any, item: any): void
    {
        const presetData = this.presetsManager.resolvePreset(item.presetId, "hideout")

        if (presetData == null)
        {
            return;
        }

        presetData.items.forEach(item => trader.assort.items.push(item))

        trader.assort.loyal_level_items[presetData.rootId] = item.loyaltyLevel

        this.setTraderItemPrice(item, trader, traderConfig, presetData.rootId)
        this.setTraderItemQuest(item, trader, presetData.rootId)
    }

    private setTraderItem(trader: ITrader, traderConfig: any, item: any): void
    {
        const rootId = this.hashUtil.generate()

        trader.assort.items.push({
            _id: rootId,
            _tpl: item.itemTemplateId,
            parentId: "hideout",
            slotId: "hideout",
            upd: {
                StackObjectsCount: item.count,
                UnlimitedCount: true,
                BuyRestrictionMax: item.count,
                BuyRestrictionCurrent: 0
            }
        })

        trader.assort.loyal_level_items[rootId] = item.loyaltyLevel

        this.setTraderItemPrice(item, trader, traderConfig, rootId)
        this.setTraderItemQuest(item, trader, rootId)
    }

    private setTraderItemPrice(item: any, trader: ITrader, traderConfig: any, rootId: string)
    {
        if (Constants.MinimumPrices)
        {
            trader.assort.barter_scheme[rootId] = [ [ { _tpl: "5449016a4bdc2d6f028b456f", count: 1 } ] ]
            return
        }

        const priceArray: IBarterScheme[] = []

        if (item.price)
        {
            for (let priceConfig of item.price)
            {
                const price: IBarterScheme = { 
                    _tpl: priceConfig.templateId, 
                    count: priceConfig.count ? priceConfig.count : 1
                }

                if (traderConfig.moneyPriceMultiplier &&
                    Helper.isMoney(priceConfig.templateId) &&
                    !Helper.isMoney(item.itemTemplateId))
                {
                    price.count = price.count * traderConfig.moneyPriceMultiplier
                }

                if (priceConfig.templateId == "59f32bb586f774757e1e8442")
                {
                    price.side = "Bear"
                    price.level = 1
                }

                if (priceConfig.templateId == "59f32c3b86f77472a31742f0")
                {
                    price.side = "Usec"
                    price.level = 1
                }

                priceArray.push(price)
            }
        }

        trader.assort.barter_scheme[rootId] = [ priceArray ]
    }

    private setTraderItemQuest(item: any, trader: ITrader, rootId: string)
    {
        if (Constants.NoQuestlockedItems)
        {
            return;
        }

        if (item.questId && item.questId != "")
        {
            if (Constants.IgnoreMockQuestlocks &&
                item.questId == "123")
            {
                return
            }

            let questState: "success" | "started" | "fail" = "success"

            if (item.questState)
            {
                questState = item.questState
            }

            trader.questassort[questState][rootId] = item.questId

            if (item.presetId && item.presetId !== "")
            {
                this.questsManager.setQuestUnlockReward({
                    itemId: rootId,
                    traderId: trader.base._id,
                    loyaltyLevel: item.loyaltyLevel,
                    presetId: item.presetId,
                    questId: item.questId,
                    questState: questState
                })
            }
            else
            {
                this.questsManager.setQuestUnlockReward({
                    itemId: rootId,
                    traderId: trader.base._id,
                    loyaltyLevel: item.loyaltyLevel,
                    templateId: item.itemTemplateId,
                    questId: item.questId,
                    questState: questState
                })
            }
        }
    }

    private static buyItem(pmcData: IPmcData, buyRequestData: IProcessBuyTradeRequestData, sessionID: string, foundInRaid: boolean, upd: Upd): IItemEventRouterResponse
    {
        const res = TradersManager.buyItemOriginal.apply(Constants.Container.resolve<TradeHelper>("TradeHelper"), [ pmcData, buyRequestData, sessionID, foundInRaid, upd ])

        if (buyRequestData.Action === "TradingConfirm" && buyRequestData.type === "buy_from_trader" && buyRequestData.tid !== "ragfair")
        {
            TradersManager.restoreTraderItem(sessionID, buyRequestData, res)
        }

        return res
    }

    private static restoreTraderItem(sessionID: string, buyRequest: IProcessBuyTradeRequestData, buyResult: IItemEventRouterResponse)
    {
        const databaseTables = Constants.Container.resolve<DatabaseServer>("DatabaseServer").getTables()

        if (!databaseTables.traders[buyRequest.tid])
        {
            return
        }

        let newItems: Product[] = []
        let profileChange: ProfileChange | null = null

        for (let changeKey in buyResult.profileChanges as TProfileChanges)
        {
            const change = buyResult.profileChanges[changeKey] as ProfileChange

            for (let itemKey in change.items.new)
            {
                const item = change.items.new[itemKey]

                newItems.push(item)
            }

            profileChange = change
        }

        if (newItems.length == 0)
        {
            return
        }

        const profile = Constants.Container.resolve<ProfileHelper>("ProfileHelper").getPmcProfile(sessionID)

        for (let newItemKey in newItems)
        {
            const newItem = newItems[newItemKey]

            const profileItemRoot = profile.Inventory.items.find(item => item._id == newItem._id)

            if (profileItemRoot == null)
            {
                return
            }

            const profileItem = Helper.getItemTree(profile.Inventory.items, profileItemRoot)
            const traderItem = TradersManager.findTraderItem(databaseTables, buyRequest.item_id, buyRequest.tid)

            if (traderItem == null || profileItem == null)
            {
                return
            }

            // Restoring item condition
            const traderItemRep = traderItem.root.upd?.Repairable

            if (traderItemRep)
            {
                if (profileItemRoot.upd)
                {
                    profileItemRoot.upd.Repairable = 
                    {
                        Durability: traderItemRep.Durability,
                        MaxDurability: traderItemRep.MaxDurability
                    }
                }
                else
                {
                    profileItemRoot.upd = 
                    {
                        Repairable: {
                            Durability: traderItemRep.Durability,
                            MaxDurability: traderItemRep.MaxDurability
                        }
                    }
                }

                newItem.upd = profileItemRoot.upd
            }

            // Restoring bullets in magazine
            const traderItemBul = traderItem.item.find(item => item.parentId == traderItem.item.find(i => i.slotId == "mod_magazine")?._id)
            const profileItemBul = profileItem.find(item => item.parentId == profileItem.find(i => i.slotId == "mod_magazine")?._id)

            if (traderItemBul && traderItemBul.upd && profileItemBul)
            {
                profileItemBul.upd = 
                {
                    StackObjectsCount: traderItemBul.upd.StackObjectsCount
                }

                const newItemBul = profileChange.items.new.find(item => item.parentId == profileChange.items.new.find(i => i.slotId == "mod_magazine")._id)
                
                if (newItemBul)
                {
                    newItemBul.upd = profileItemBul.upd
                }
            }
        }
        
    }

    private static findTraderItem(databaseTables: IDatabaseTables, itemId: string, traderId: string): { root: Item, item: Item[] } | null
    {
        const root = databaseTables.traders[traderId].assort.items.find(item => item._id == itemId)

        if (!root)
        {
            return null
        }

        const item = Helper.getItemTree(databaseTables.traders[traderId].assort.items, root)

        return { root, item }
    }
}