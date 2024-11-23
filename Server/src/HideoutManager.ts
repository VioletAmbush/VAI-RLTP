import { AbstractModManager } from "./AbstractModManager"
import { RandomUtil } from "@spt/utils/RandomUtil"
import { Constants } from "./Constants"
import { DependencyContainer } from "tsyringe"
import { HashUtil } from "@spt/utils/HashUtil"

export class HideoutManager extends AbstractModManager
{
    protected configName: string = "HideoutConfig"

    private hashUtil: HashUtil
    
    protected postDBInitialize(container: DependencyContainer): void 
    {
        super.postDBInitialize(container)

        this.hashUtil = container.resolve<HashUtil>("HashUtil")
    }

    protected afterPostDB(): void
    {
        if (this.config.removeGeneratorStageOneSecurityRequirement)
        {
            var waterCollector = this.databaseTables.hideout.areas.find(area => area.type == 4)
            
            if (waterCollector)
            {
                waterCollector.stages["1"].requirements
                    .filter(req => req.type == "Area" && req.areaType == 1)
                    .forEach(req => req.requiredLevel = 0)
            }
        }

        if (this.config.removeWaterCollectorStageOneSecurityRequirement)
        {
            var waterCollector = this.databaseTables.hideout.areas.find(area => area.type == 6)
            
            if (waterCollector)
            {
                waterCollector.stages["1"].requirements
                    .filter(req => req.type == "Area" && req.areaType == 1)
                    .forEach(req => req.requiredLevel = 0)
            }
        }

        for (let areaKey in this.config.areaTypes)
        {
            const areaConfig = this.config.areaTypes[areaKey]
            const area = this.databaseTables.hideout.areas.find(area => area.type.toString() == areaKey)

            if (!area)
            {
                continue
            }

            if (areaConfig.clearCrafts == true)
            {
                this.databaseTables.hideout.production = 
                    this.databaseTables.hideout.production.filter(prod => prod.areaType != area.type)
            }

            if (areaConfig.stageItemRequirements)
            {
                for (let stageKey in area.stages)
                {
                    const stage = area.stages[stageKey]
                    const stageConfig = areaConfig.stageItemRequirements[stageKey]
    
                    if (!stage || !stageConfig)
                    {
                        continue
                    }
    
                    stage.requirements = stage.requirements.filter(req => req.type != "Item")
    
                    stage.requirements.push(...stageConfig.map(reqConfig => 
                    {
                        return { 
                            templateId: reqConfig.templateId,
                            count: reqConfig.count,
                            isFunctional: false,
                            isEncoded: false,
                            type: "Item"
                        }
                    }))
                }
            }

            if (areaConfig.stageAreaRequirements)
            {
                for (let stageKey in area.stages)
                {
                    const stage = area.stages[stageKey]
                    const stageConfig = areaConfig.stageAreaRequirements[stageKey]
    
                    if (!stage || !stageConfig)
                    {
                        continue
                    }
    
                    stage.requirements = stage.requirements.filter(req => req.type != "Area")

                    stage.requirements.push(...stageConfig.map(reqConfig => 
                    {
                        return { 
                            areaType: reqConfig.type,
                            requiredLevel: reqConfig.level,
                            type: "Area",
                            isEncoded: false
                        }
                    }))
                }
            }

            if (areaConfig.stageCrafts)
            {
                for (let stageKey in areaConfig.stageCrafts)
                {
                    const stageConfig = areaConfig.stageCrafts[stageKey]
                    
                    if (!stageConfig)
                    {
                        continue
                    }

                    for (let craftKey in stageConfig)
                    {
                        const craftConfig = stageConfig[craftKey]

                        const craft = {
                            _id: craftConfig.id,
                            areaType: area.type,
                            productionTime: craftConfig.time,
                            endProduct: craftConfig.resultId,
                            count: craftConfig.resultCount,

                            requirements: [],

                            needFuelForAllProductionTime: false,
                            locked: false,
                            continuous: false,
                            productionLimitCount: 0,
                            isEncoded: false
                        }

                        craft.requirements.push({
                            areaType: area.type,
                            requiredLevel: +stageKey,
                            type: "Area"
                        })

                        craft.requirements.push(...craftConfig.requirements.map(reqConfig => {
                            return {
                                templateId: reqConfig.templateId,
                                count: reqConfig.count,
                                type: reqConfig.tool == true ? "Tool" : "Item", 
                                isFunctional: false,
                                isEncoded: false
                            }
                        }))
                        
                        this.databaseTables.hideout.production.push(craft)
                    }
                }
            }

        }

        this.logger.info(`${Constants.ModTitle}: Hideout changes applied!`)
    }

    public getAreaBonus(type: string, stage: string, target: "death" | "survive", randomUtil: RandomUtil): AreaBonus[]
    {
        const areaConfig = this.config.areaTypes[type]

        if (!areaConfig)
        {
            return []
        }

        let stageConfig: any = undefined

        if (target == "death")
        {
            stageConfig = areaConfig.stageDeathBonuses[stage] 
        }

        if (target == "survive")
        {
            stageConfig = areaConfig.stageSurviveBonuses[stage]
        }

        if (!stageConfig)
        {
            return []
        }

        let bonuses: AreaBonus[] = []

        const bonusIndex = randomUtil.getInt(0, stageConfig.length - 1)

        for (let bonus of stageConfig[bonusIndex])
        {
            bonuses.push(
            {
                templateId: bonus.templateId,
                amountMin: bonus.amountMin,
                amountMax: bonus.amountMax
            })
        }

        return bonuses
    }
}

export class AreaBonus 
{
    public templateId: string
    public amountMin: number
    public amountMax: number
}