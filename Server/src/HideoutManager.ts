import { AbstractModManager } from "./AbstractModManager"
import { RandomUtil } from "@spt/utils/RandomUtil"
import { Constants } from "./Constants"
import { DependencyContainer } from "tsyringe"
import { HideoutAreas } from "@spt/models/enums/HideoutAreas"

export class HideoutManager extends AbstractModManager
{
    protected configName: string = "HideoutConfig"
    
    protected postDBInitialize(container: DependencyContainer): void 
    {
        super.postDBInitialize(container)
    }

    protected afterPostDB(): void
    {
        if (this.config.removeGeneratorStageOneSecurityRequirement)
        {
            var waterCollector = this.databaseTables.hideout.areas.find(area => area.type == HideoutAreas.GENERATOR)
            
            if (waterCollector)
            {
                waterCollector.stages["1"].requirements
                    .filter(req => req.type == "Area" && req.areaType == HideoutAreas.SECURITY)
                    .forEach(req => req.requiredLevel = 0)
            }
        }

        if (this.config.removeWaterCollectorStageOneSecurityRequirement)
        {
            var waterCollector = this.databaseTables.hideout.areas.find(area => area.type == HideoutAreas.WATER_COLLECTOR)
            
            if (waterCollector)
            {
                waterCollector.stages["1"].requirements
                    .filter(req => req.type == "Area" && req.areaType == HideoutAreas.SECURITY)
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
                this.databaseTables.hideout.production.recipes = 
                    this.databaseTables.hideout.production.recipes.filter(prod => prod.areaType != area.type)
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
                            productionTime: Constants.InstantCrafting == true ? 1 : craftConfig.time,
                            endProduct: craftConfig.resultId,
                            count: craftConfig.resultCount,

                            requirements: [],

                            needFuelForAllProductionTime: false,
                            locked: false,
                            continuous: false,
                            productionLimitCount: 0,
                            isEncoded: false,
                            isCodeProduction: false
                        }

                        craft.requirements.push({
                            areaType: area.type,
                            requiredLevel: +stageKey,
                            type: "Area"
                        })

                        if (Constants.EasyCrafting)
                        {
                            craft.requirements.push({
                                templateId: "5449016a4bdc2d6f028b456f",
                                count: 1,
                                type: "Item", 
                                isFunctional: false,
                                isEncoded: false
                            })
                        }
                        else
                        {
                            craft.requirements.push(...craftConfig.requirements.map(reqConfig => {
                                return {
                                    templateId: reqConfig.templateId,
                                    count: reqConfig.count,
                                    type: reqConfig.tool == true ? "Tool" : "Item", 
                                    isFunctional: false,
                                    isEncoded: false
                                }
                            }))
                        }
                        
                        this.databaseTables.hideout.production.recipes.push(craft)
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