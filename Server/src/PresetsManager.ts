import { IItem } from "@spt/models/eft/common/tables/IItem"
import { RandomUtil } from "@spt/utils/RandomUtil"
import { AbstractModManager } from "./AbstractModManager"
import { Constants } from "./Constants"
import { DependencyContainer } from "tsyringe"
import { HashUtil } from "@spt/utils/HashUtil"


export class PresetsManager extends AbstractModManager
{
    protected configName: string = "PresetsConfig"

    private hashUtil: HashUtil

    protected postDBInitialize(container: DependencyContainer): void 
    {
        super.postDBInitialize(container)

        this.hashUtil = container.resolve<HashUtil>("HashUtil")
    }

    protected afterPostDB(): void 
    {
        let presetCount = 0

        for (let presetId in this.config.items)
        {
            this.setPreset(presetId)

            presetCount++
        }

        for (let categoryKey in this.config.categorizedItems)
        {
            for (let presetId in this.config.categorizedItems[categoryKey])
            {
                this.setPreset(presetId)

                presetCount++
            }
        }

        if (Constants.PrintPresetCount)
        {
            this.logger.info(`${Constants.ModTitle}: Added ${presetCount} presets.`)
        }
    }

    private setPreset(presetId: string)
    {
        const preset = this.resolvePreset(presetId)
        presetId = presetId.replace('_', ' ').toUpperCase()

        const changeName: boolean = !(
            presetId.includes("MOUNTED") ||
            presetId.includes("SHADE") ||
            presetId.includes("EYECUP") ||
            presetId.includes("COLLIMATOR"))

        this.databaseTables.globals.ItemPresets[presetId] = {
            _changeWeaponName: changeName,
            _id: presetId,
            _name: presetId,
            _parent: preset.rootId,
            _type: "Preset",
            _items: preset.items
        }
    }

    public resolveRandomPreset(categories: any[], excludedPresets: string[], includedPresets: string[], parentId: string): { rootId: string, items: IItem[] } | null
    {
        const randomUtil: RandomUtil = Constants.getRandomUtil()

        if (categories.find(cat => cat == "__all__"))
        {
            categories = Object.keys(this.config.categorizedItems)
        }

        if (categories.find(cat => cat == "__all__weapon__"))
        {
            categories = this.config.weaponCategories
        }

        if (categories.find(cat => cat == "__all__armor__"))
        {
            categories = this.config.armorCategories
        }

        if (categories.find(cat => cat == "__all__equipment__"))
        {
            categories = this.config.equipmentCategories
        }

        let presets: string[] = includedPresets == undefined || includedPresets == null ? [] : includedPresets

        for (let categoryName of categories)
        {
            const category = this.config.categorizedItems[categoryName]

            for (let presetKey in category)
            {
                if (!presets.includes(presetKey))
                    presets.push(presetKey)

            }
        }

        if (excludedPresets && excludedPresets.length > 0)
        {
            presets = presets.filter(p => excludedPresets.includes(p))
        }

        let randomPreset = this.resolvePreset(presets[randomUtil.getInt(0, presets.length - 1)], parentId)
        let failsafeCounter = 0

        while (!randomPreset)
        {
            failsafeCounter++

            randomPreset = this.resolvePreset(presets[randomUtil.getInt(0, presets.length - 1)], parentId)

            if (failsafeCounter > 10)
            {
                break
            }
        }

        if (!randomPreset)
        {
            Constants.getLogger().error(`${Constants.ModTitle}: Could not find random preset!`)

            return null
        }

        return randomPreset
    }

    public resolveAllPresetIds(): string[]
    {
        let result = []

        for (let presetKey in this.config.items)
        {
            result.push(presetKey)
        }

        for (let categoryKey in this.config.categorizedItems)
        {
            for (let presetKey in this.config.categorizedItems[categoryKey])
            {
                result.push(presetKey)
            }
        }

        return result
    }

    public resolvePreset(presetId: string, parentId: string = null): { rootId: string, items: IItem[] } | null
    {
        let presetConfig = this.config.items[presetId]

        if (!presetConfig)
        {
            for (let categoryConfigKey in this.config.categorizedItems)
            {
                const categoryConfig = this.config.categorizedItems[categoryConfigKey]

                presetConfig = categoryConfig[presetId]

                if (presetConfig)
                {
                    break
                }
            }
        }

        if (!presetConfig)
        {
            Constants.getLogger().error(`${Constants.ModTitle}: Could not find preset ${presetId}`)

            return null
        }

        let preset = this.resolvePresetImpl(presetConfig, parentId)

        preset = this.regeneratePresetIds(preset, presetId)

        this.setPresetDurability(presetId, preset.items.find(i => i._id == preset.rootId))

        return preset
    }

    private resolvePresetImpl(config: any, parentId: string = null): { rootId: string, items: IItem[] } | null
    {
        let presetRoot = config.find(p => !p.parentId || !config.find(item => item._id === p.parentId))

        if (!presetRoot)
        {
            Constants.getLogger().error(`${Constants.ModTitle}: Could not find root item for preset ${Constants.getJsonUtil().serialize(config)}`)

            return null
        }

        const result: IItem[] = []

        const rootItem: IItem = {
            _id: presetRoot._id,
            _tpl: presetRoot._tpl,
            slotId: "hideout",
            upd: presetRoot.upd
        }

        if (parentId)
        {
            rootItem.parentId = parentId
        }

        result.push(rootItem)

        for (let item of config)
        {
            if (item._tpl === presetRoot._tpl)
            {
                continue
            }

            result.push({
                _id: item._id,
                _tpl: item._tpl,
                slotId: item.slotId,
                parentId: item.parentId,
                upd: item.upd
            })
        }

        return { rootId: rootItem._id, items: result }
    }

    private setPresetDurability(presetId: string, presetRoot: IItem)
    {
        if (this.config.alwaysFullDurability == true)
        {
            presetRoot.upd.Repairable = {
                Durability: 100,
                MaxDurability: 100
            }
            
            return
        }

        if (presetRoot.upd.Repairable)
            return
        
        if (presetId.toLowerCase().includes("basic"))
        {
            presetRoot.upd.Repairable = {
                Durability: this.config.basicDurability,
                MaxDurability: this.config.basicMaxDurability
            }

            return
        }
        
        if (presetId.toLowerCase().includes("std"))
        {
            presetRoot.upd.Repairable = {
                Durability: this.config.stdDurability,
                MaxDurability: this.config.stdMaxDurability
            }

            return
        }
        
        if (presetId.toLowerCase().includes("adv"))
        {
            presetRoot.upd.Repairable = {
                Durability: this.config.advDurability,
                MaxDurability: this.config.advMaxDurability
            }

            return
        }
        
        if (presetId.toLowerCase().includes("sup"))
        {
            presetRoot.upd.Repairable = {
                Durability: this.config.supDurability,
                MaxDurability: this.config.supMaxDurability
            }

            return
        }
        
        if (presetId.toLowerCase().includes("master"))
        {
            presetRoot.upd.Repairable = {
                Durability: this.config.masterDurability,
                MaxDurability: this.config.masterMaxDurability
            }

            return
        }
        
        if (presetId.toLowerCase().includes("melee barter"))
        {
            presetRoot.upd.Repairable = {
                Durability: this.config.meleeBarterDurability,
                MaxDurability: this.config.meleeBarterMaxDurability
            }

            return
        }
        
        if (presetId.toLowerCase().includes("contractor"))
        {
            presetRoot.upd.Repairable = {
                Durability: this.config.contractorDurability,
                MaxDurability: this.config.contractorMaxDurability
            }

            return
        }
        
        if (presetId.toLowerCase().includes("info barter"))
        {
            presetRoot.upd.Repairable = {
                Durability: this.config.infoBarterDurability,
                MaxDurability: this.config.infoBarterMaxDurability
            }

            return
        }
        
        if (presetId.toLowerCase().includes("btc barter"))
        {
            presetRoot.upd.Repairable = {
                Durability: this.config.BTCBarterDurability,
                MaxDurability: this.config.BTCBarterMaxDurability
            }

            return
        }
        
        if (presetId.toLowerCase().includes("edition"))
        {
            presetRoot.upd.Repairable = {
                Durability: this.config.bossWeaponDurability,
                MaxDurability: this.config.bossWeaponMaxDurability
            }

            return
        }
    }

    private regeneratePresetIds(preset: { rootId: string, items: IItem[] }, presetId: string) : { rootId: string, items: IItem[] }
    {
        let presetJson = this.jsonUtil.serialize(preset)

        preset.items.forEach(i => 
        {
            presetJson = presetJson.replaceAll(i._id, this.hashUtil.generate())
        })

        return this.jsonUtil.deserialize<{ rootId: string, items: IItem[] }>(presetJson)
    }
}