import { HashUtil } from "@spt/utils/HashUtil"
import { AbstractModManager } from "./AbstractModManager"
import { Constants } from "./Constants"

export class GlobalsManager extends AbstractModManager
{
    protected configName: string = "GlobalsConfig"

    protected afterPostDB(): void
    {
        this.logger.debug(this.jsonUtil.serialize(this.databaseTables.globals.ItemPresets))

        if (Constants.PrintNewHashes)
        {
            const hashUtil = this.container.resolve<HashUtil>("HashUtil")
    
            for (let i = 0; i < 100; i++)
            {
                console.log(hashUtil.generate())
            }
        }

        if (this.config.removeStatusEffectRemovePrices == true)
        {
            this.databaseTables.globals.config.Health.Effects.BreakPart.RemovePrice = 0
            this.databaseTables.globals.config.Health.Effects.Fracture.RemovePrice = 0
            this.databaseTables.globals.config.Health.Effects.LightBleeding.RemovePrice = 0
            this.databaseTables.globals.config.Health.Effects.HeavyBleeding.RemovePrice = 0
            this.databaseTables.globals.config.Health.Effects.Intoxication.RemovePrice = 0
        }

        if (this.config.removePostRaidHeal == true)
        {
            for (let traderKey in this.databaseTables.traders)
            {
                const trader = this.databaseTables.traders[traderKey]

                trader.base.medic = false
            }
        }
        
        this.databaseTables.globals.config.RagFair.enabled = !(this.config.disableFlea == true)

        if (this.config.disableFleaBlacklist == true)
        {
            for (let itemKey in this.databaseTables.templates.items)
            {
                const item = this.databaseTables.templates.items[itemKey]

                item._props.CanSellOnRagfair = true
                item._props.CanRequireOnRagfair = true
            }
            
        }

        if (this.config.removeTraderMoneyRequirements == true)
        {
            for (let traderKey in this.databaseTables.traders)
            {
                const trader = this.databaseTables.traders[traderKey]

                trader.base.loyaltyLevels.forEach(level => 
                {
                    level.minSalesSum = 0
                })
            }
        }

        if (this.config.delayScavRun == true)
        {
            this.databaseTables.globals.config.SavagePlayCooldown = 2147483646
        }

        this.logger.info(`${Constants.ModTitle}: Globals changes applied!`)
    }
}