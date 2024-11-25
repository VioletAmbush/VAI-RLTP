import { AbstractModManager } from "./AbstractModManager"
import { QuestRewardType } from "@spt/models/enums/QuestRewardType"
import { DependencyContainer } from "tsyringe"
import { HashUtil } from "@spt/utils/HashUtil"
import { IQuestCondition, IQuest, IQuestReward } from "@spt/models/eft/common/tables/IQuest"
import { PresetsManager } from "./PresetsManager"
import { WeaponsManager } from "./WeaponsManager"
import { Constants } from "./Constants"
import { QuestTypeEnum } from "@spt/models/enums/QuestTypeEnum"
import { QuestStatus } from "@spt/models/enums/QuestStatus"
import { Helper } from "./Helper"
import { LocaleManager } from "./LocaleManager"
import { ConfigServer } from "@spt/servers/ConfigServer"
import { ConfigTypes } from "@spt/models/enums/ConfigTypes"
import { IQuestConfig } from "@spt/models/spt/config/IQuestConfig"
import { Item } from "@spt/models/eft/common/tables/IItem"
 
export class QuestRewardRequest 
{
    questId: string
    loyaltyLevel: number
    traderId: string
    itemId: string
    templateId?: string
    presetData?: { rootId: string, items: Item[] }
    questState: "success" | "started" | "fail"
}

export class QuestsManager extends AbstractModManager
{
    protected configName: string = "QuestsConfig"

    private presetsManager: PresetsManager
    private weaponsManager: WeaponsManager
    private localeManager: LocaleManager
    private hashUtil: HashUtil
    private questsConfig: IQuestConfig

    private readyToSetUnlocks: boolean = false

    private setRequestQueue: QuestRewardRequest[] = []

    public override priority: number = 2

    constructor(
        presetsManager: PresetsManager, 
        weaponsManager: WeaponsManager,
        localeManager: LocaleManager)
    {
        super()

        this.presetsManager = presetsManager
        this.weaponsManager = weaponsManager
        this.localeManager = localeManager
    }

    protected postDBInitialize(container: DependencyContainer): void
    {
        super.postDBInitialize(container)

        this.hashUtil = container.resolve<HashUtil>("HashUtil")
        this.questsConfig = container.resolve<ConfigServer>("ConfigServer").getConfig<IQuestConfig>(ConfigTypes.QUEST)
    }

    protected afterPostDB(): void
    {
        let questCount = 0

        if (this.config.removeRewards == true)
        {
            for (let questKey in this.databaseTables.templates.quests)
            {
                const quest = this.databaseTables.templates.quests[questKey]
                
                quest.rewards.Success = quest.rewards.Success.filter(reward => 
                    reward.type !== QuestRewardType.ITEM &&
                    reward.type !== QuestRewardType.PRODUCTIONS_SCHEME &&
                    reward.type !== QuestRewardType.ASSORTMENT_UNLOCK)
            }
        }

        for (let questId in this.config.quests)
        {
            const quest = this.databaseTables.templates.quests[questId]

            if (!quest)
            {
                continue
            }

            const questConfig = this.config.quests[questId]
            
            if (questConfig.clearAllRewards == true ||
                (questConfig.rewards && questConfig.rewards.length > 0))
            {
                this.setQuestRewards(quest, questConfig.rewards, questConfig.clearAllRewards)
            }

            if (questConfig.requirements &&
                questConfig.requirements.length > 0)
            {
                this.setQuestFinishReqs(quest, "", questConfig.requirements.map(r => 
                {
                    return {
                        parent: r.type,
                        type: r.type,
                        count: r.count,
                        target: r.templateId,
                        location: "any",
                        weaponIds: r.weaponIds,
                        weaponCategories: r.weaponCategories,
                        scavTypes: r.scavTypes
                    }
                }), false)
            }
        }

        for (let questConfigKey in this.config.masteryQuests)
        {
            const questConfig = this.config.masteryQuests[questConfigKey]

            if (!questConfig.id || questConfig.id == "")
            {
                continue
            }

            const quest = this.setMasteryQuest(questConfig, questConfigKey)

            if (!quest)
            {
                continue
            }

            if (questConfig.startQuestRequirements)
            {
                this.setQuestStartReqs(quest, questConfig)
            }
            
            this.setMasteryQuestFinishReqs(quest, questConfig)
            this.setQuestRewards(quest, questConfig.rewards, questConfig.clearAllRewards)

            questCount++
        }

        for (let questConfigKey in this.config.collectorQuests)
        {
            const questConfig = this.config.collectorQuests[questConfigKey]

            if (!questConfig.id || questConfig.id == "")
            {
                continue
            }

            const quest = this.setCollectorQuest(questConfig, questConfigKey)

            if (!quest)
            {
                continue
            }

            if (questConfig.startQuestRequirements)
            {
                this.setQuestStartReqs(quest, questConfig)
            }
            
            this.setCollectorQuestFinishReqs(quest, questConfig)
            this.setQuestRewards(quest, questConfig.rewards, questConfig.clearAllRewards)

            questCount++
        }

        if (Constants.EasyQuests)
        {
            for (let questKey in this.databaseTables.templates.quests)
            {
                const quest = this.databaseTables.templates.quests[questKey]
                
                this.setQuestFinishReqs(quest, "", [
                {
                    parent: "HandoverItem",
                    type: "HandoverItem",
                    target: "5449016a4bdc2d6f028b456f",
                    location: "any",
                    description: "Bring 1 rouble",
                    count: 1
                }])
            }
        }

        this.readyToSetUnlocks = true

        this.setRequestQueue.forEach(req => this.setQuestUnlockReward(req))

        if (Constants.PrintQuestCount)
        {
            this.logger.info(`${Constants.ModTitle}: Quest changes applied! (${questCount} quests added)`)
        }
        else
        {
            this.logger.info(`${Constants.ModTitle}: Quest changes applied!`)
        }
    }

    protected afterPostSpt(): void 
    {
        if (this.config.disableRepeatableQuests == true)
        {
            this.questsConfig.repeatableQuests.forEach(rq => 
            {
                rq.minPlayerLevel = 100
            })
        }
    }
    
    private setMasteryQuest(questConfig: any, questTitle: string): IQuest
    {
        if (this.databaseTables.templates.quests[questConfig.id])
        {
            this.logger.error(`${Constants.ModTitle}: Quest with id ${questConfig.id} already exists`)
            return
        }

        let quest: IQuest = {
            _id: questConfig.id,
            //templateId: questConfig.id,
            traderId: Helper.acronymToTraderId(questConfig.traderAcr),
            type: QuestTypeEnum.ELIMINATION,

            canShowNotificationsInGame: true,
            restartable: false,
            instantComplete: false,
            isKey: false,
            secretQuest: false,

            //status: QuestStatus.Locked,
            //questStatus: QuestStatus.Locked,
            //sptStatus: QuestStatus.Locked,
            
            image: "/files/quest/icon/5968ec2986f7741ddf17db83.png",
            location: "any",
            side: "Pmc",

            rewards: {
                Started: [],
                Success: [],
                Fail: []
            },
            conditions: {
                AvailableForFinish: [],
                AvailableForStart: [],
                Fail: []
            },

            name: `${questConfig.id}_title`,
            QuestName: `${questConfig.id}_title`,
            description: `${questConfig.id}_description`,

            note: `Note ${questTitle}`,
            startedMessageText: `Started ${questTitle}`,
            acceptPlayerMessage: `Accepted ${questTitle}`,
            declinePlayerMessage: `Declined ${questTitle}`,
            successMessageText: `Succeeded ${questTitle}`,
            failMessageText: `Failed ${questTitle}`,
            changeQuestMessageText: `Changed ${questTitle}`,
            completePlayerMessage: `Completed ${questTitle}`,
        }

        this.localeManager.setENLocale(`${questConfig.id}_title`, questTitle)

        this.databaseTables.templates.quests[questConfig.id] = quest

        return quest
    }

    private setCollectorQuest(questConfig: any, questTitle: string): IQuest
    {
        if (this.databaseTables.templates.quests[questConfig.id])
        {
            this.logger.error(`${Constants.ModTitle}: Quest with id ${questConfig.id} already exists`)
            return
        }

        let quest: IQuest = {
            _id: questConfig.id,
            //templateId: questConfig.id,
            traderId: Helper.acronymToTraderId(questConfig.traderAcr),
            type: QuestTypeEnum.PICKUP,

            canShowNotificationsInGame: true,
            restartable: false,
            instantComplete: false,
            isKey: false,
            secretQuest: false,

            //status: QuestStatus.Locked,
            //questStatus: QuestStatus.Locked,
            //sptStatus: QuestStatus.Locked,
            
            image: "/files/quest/icon/60c37450de6b0b44cc320e9a.jpg",
            location: "any",
            side: "Pmc",

            rewards: {
                Started: [],
                Success: [],
                Fail: []
            },
            conditions: {
                AvailableForFinish: [],
                AvailableForStart: [],
                Fail: []
            },

            name: `${questConfig.id}_title`,
            QuestName: `${questConfig.id}_title`,
            description: `${questConfig.id}_description`,

            note: `Note ${questTitle}`,
            startedMessageText: `Started ${questTitle}`,
            acceptPlayerMessage: `Accepted ${questTitle}`,
            declinePlayerMessage: `Declined ${questTitle}`,
            successMessageText: `Succeeded ${questTitle}`,
            failMessageText: `Failed ${questTitle}`,
            changeQuestMessageText: `Changed ${questTitle}`,
            completePlayerMessage: `Completed ${questTitle}`,
        }

        this.localeManager.setENLocale(`${questConfig.id}_title`, questTitle)

        this.databaseTables.templates.quests[questConfig.id] = quest

        return quest
    }

    private setQuestStartReqs(quest: IQuest, questConfig: any)
    {
        if (Constants.NoNewQuestsStartRequirements)
            return

        if (!questConfig.startQuestRequirements || questConfig.startQuestRequirements.length == 0)
            return;

        quest.conditions.AvailableForStart.length = 0

        let index = 0

        for (let reqQuestId of questConfig.startQuestRequirements)
        {
            quest.conditions.AvailableForStart.push({
                id: `${questConfig.id}AFS${index}`,
                availableAfter: 0,
                dispersion: 0,
                conditionType: "Quest",
                globalQuestCounterId: "",
                dynamicLocale: false,
                index: 0,
                parentId: "",
                status: [ QuestStatus.Success ],
                target: reqQuestId,
                visibilityConditions: []
            })

            index++
        }
    }
    
    private setMasteryQuestFinishReqs(quest: IQuest, questConfig: any)
    {
        this.setQuestFinishReqs(quest, questConfig.description, [{
            type: QuestTypeEnum.ELIMINATION,
            count: questConfig.kills,
            weaponIds: questConfig.weaponIds,
            weaponCategories: questConfig.weaponCategories,
            scavTypes: questConfig.scavTypes
        }])
    }

    private setCollectorQuestFinishReqs(quest: IQuest, questConfig: any)
    {
        let reqs = []

        for (let reqId in questConfig.requirements)
        {
            const req = questConfig.requirements[reqId]

            reqs.push({
                templateId: req.templateId,
                count: req.count
            })
        }

        this.setQuestFinishReqs(quest, questConfig.description, reqs.map(r => 
        {
            return {
                parent: "HandoverItem",
                type: "HandoverItem",
                location: "any",
                target: r.templateId,
                count: r.count
            }
        }))
    }

    private setQuestFinishReqs(quest: IQuest, questDescription: string, finishRequirements: any[], setLocale: boolean = true)
    {
        quest.conditions.AvailableForFinish.length = 0

        if (!finishRequirements || finishRequirements.length == 0)
            return

        let index = 0
        let description = questDescription ?? "";

        for (let reqConfig of finishRequirements)
        {
            let req: IQuestCondition
            let reqId: string = `${quest._id}req${index}`

            if (reqConfig.description)
            {
                description += `${reqConfig.description}\n`
            }

            if (reqConfig.type == QuestTypeEnum.ELIMINATION)
            {
                req = {
                    id: reqId,
                    type: QuestTypeEnum.ELIMINATION,
                    conditionType: "CounterCreator",
                    index: index,
                    value: reqConfig.count,

                    completeInSeconds: 0,
                    doNotResetIfCounterCompleted: false,
                    dynamicLocale: false,
                    globalQuestCounterId: "",
                    oneSessionOnly: false,
                    parentId: "",
                    visibilityConditions: [],

                    counter: {
                        id: `${reqId}_counter`,
                        conditions: [
                            {
                                id: `${reqId}_condition`,
                                dynamicLocale: false,
                                compareMethod: ">=",
                                target: "Any",
                                value: 1,
                                conditionType: "Kills",
                                bodyPart: [],
                                daytime: {
                                    from: 0,
                                    to: 0,
                                },
                                distance: {
                                    compareMethod: ">=",
                                    value: 0
                                },
                                enemyEquipmentExclusive: [],
                                enemyEquipmentInclusive: [],
                                enemyHealthEffects: [],
                                resetOnSessionEnd: false,
                                savageRole: [],
                                weapon: [],
                                weaponCaliber: [],
                                weaponModsExclusive: [],
                                weaponModsInclusive: [],
                            }
                        ]
                    }
                }

                if (reqConfig.scavTypes && reqConfig.scavTypes.length > 0)
                {
                    description += `Kill ${reqConfig.scavTypes
                        .map(scavType => Helper.scavTypeToString(scavType))
                        .join(", ")}\n`

                    req.counter.conditions.forEach(cond => {
                        cond.savageRole = reqConfig.scavTypes
                    })
                }

                if (reqConfig.weaponIds && reqConfig.weaponIds.length > 0)
                {
                    req.counter.conditions.forEach(cond => 
                    {
                        cond.weapon = reqConfig.weaponIds
                    })

                    description += `Kill with any of: ${reqConfig.weaponIds
                        .map(id => this.weaponsManager.getWeaponDescription(id))
                        .join(", ")}\n`
                }
                else if (reqConfig.weaponCategories && reqConfig.weaponCategories.length > 0)
                {
                    let ids: { key: string, desc: string }[] = []

                    for (let categoryId in reqConfig.weaponCategories)
                    {
                        const category = reqConfig.weaponCategories[categoryId]

                        ids.push(...this.weaponsManager.getWeaponCategoryIds(category))
                    }

                    req.counter.conditions.forEach(cond => 
                    {
                        cond.weapon = ids.map(kvp => kvp.key)
                    })

                    description += `Kill with any of: ${ids
                        .map(kvp => kvp.desc)
                        .join(", ")}\n`
                }

                // if (reqConfig.location && reqConfig.location != "any")
                // {
                //     req.counter.conditions.push({
                //         id: this.hashUtil.generate(),
                //         target: [ reqConfig.location ],
                //         value: "",
                //         compareMethod: ""
                //     })
                // }
                
                if (reqConfig.scavTypes && reqConfig.scavTypes.length > 0)
                {
                    this.localeManager.setENLocale(reqId, `Kill ${reqConfig.scavTypes
                        .map(scavType => Helper.scavTypeToString(scavType))
                        .join(", ")}`)
                }
                else
                {
                    this.localeManager.setENLocale(reqId, `Kill any`)
                }
            }

            if (reqConfig.type == "HandoverItem")
            {
                quest.type = QuestTypeEnum.COMPLETION

                description += `Find ${reqConfig.count} ${this.localeManager.getENLocale(reqConfig.target)}\n`

                req = {
                    id: reqId,
                    index: index,
                    conditionType: "HandoverItem",

                    maxDurability: 100,
                    minDurability: 0,
                    dogtagLevel: 0,
                    value: reqConfig.count,
                    onlyFoundInRaid: false,
                    target: [
                        reqConfig.target
                    ],

                    dynamicLocale: false,
                    isEncoded: false,
                    globalQuestCounterId: "",
                    parentId: "",
                    visibilityConditions: []
                }

                this.localeManager.setENLocale(
                    reqId, 
                    `Handover ${reqConfig.count} ${this.localeManager.getENLocale(reqConfig.target)}`)
            }

            quest.conditions.AvailableForFinish.push(req)

            index++
        }

        if (setLocale)
            this.localeManager.setENLocale(`${quest._id}_description`, description)

        this.databaseTables.templates.quests[quest._id] = quest
    }

    private setQuestRewards(quest: IQuest, rewards: any[], clearAllRewards: boolean | undefined = undefined)
    {
        if (clearAllRewards == true)
            quest.rewards.Success.length = 0

        if (!rewards || rewards.length == 0)
            return;

        let index =  quest.rewards.Success.length == 0 ? 0 : 
            Math.max(...quest.rewards.Success.map(r => r.index)) + 1

        for (let rewConfig of rewards)
        {
            const reward: IQuestReward = {
                id: `${quest._id}reward${index}`,
                value: rewConfig.count.toString(),
                type: rewConfig.type ? rewConfig.type : "Item",
                index: index,
                target: `${quest._id}reward${index}target`,
                items: [
                    {
                        _id: `${quest._id}reward${index}target`,
                        _tpl: rewConfig.templateId,
                        upd: {
                            StackObjectsCount: rewConfig.count
                        }
                    }
                ]
            }

            quest.rewards.Success.push(reward)

            index++
        }
    }

    public setQuestUnlockReward(request: QuestRewardRequest)
    {
        if (!this.readyToSetUnlocks)
        {
            this.setRequestQueue.push(request)
            return
        }

        const quest = this.databaseTables.templates.quests[request.questId]

        if (!quest)
            return

        const rewards = 
            request.questState == "success" ? quest.rewards.Success : 
            request.questState == "started" ? quest.rewards.Started : 
            quest.rewards.Fail

        let index =  rewards.length == 0 ? 0 : 
            Math.max(...rewards.map(r => r.index)) + 1

        let items = request.presetData ? 
            request.presetData.items :
            [{
                _id: request.itemId,
                _tpl: request.templateId
            }]

        const reward: IQuestReward = {
            id: `${quest._id}reward${index}`,
            index: index,
            items: items,
            loyaltyLevel: request.loyaltyLevel,
            target: request.itemId,
            traderId: request.traderId,
            type: QuestRewardType.ASSORTMENT_UNLOCK
        }

        rewards.push(reward)
    }
}