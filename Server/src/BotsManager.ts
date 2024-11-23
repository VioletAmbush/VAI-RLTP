import { AbstractModManager } from "./AbstractModManager"

export class BotsManager extends AbstractModManager
{
    protected configName: string = "BotsConfig"

    protected override afterPostDB(): void 
    {
        for (let botKey in this.config.bots)
        {
            const botConfig = this.config.bots[botKey]
            const bot = this.databaseTables.bots.types[botKey]

            if (botConfig.lootMultipliers && botConfig.lootMultipliers.length > 0)
            {
                for (let lmConfigKey in botConfig.lootMultipliers)
                {
                    const lmConfig = botConfig.lootMultipliers[lmConfigKey]
    
                    for (let itemKey in bot.inventory.items)
                    {
                        const inv = bot.inventory.items[itemKey]
        
                        if (lmConfig.templateId in inv)
                        {
                            inv[lmConfig.templateId] = Math.max(1, Math.floor(inv[lmConfig.templateId] * lmConfig.multiplier))
                        }
                    }
                }
            }
        }
    }
}