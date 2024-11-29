import { ITemplateItem } from "@spt/models/eft/common/tables/ITemplateItem";
import { AbstractModManager } from "./AbstractModManager";
import { Helper } from "./Helper";
import { RecursiveCloner } from "@spt/utils/cloners/RecursiveCloner"
import { DependencyContainer } from "tsyringe";
import { LocaleManager } from "./LocaleManager";


export class ItemsManager extends AbstractModManager
{
    protected configName: string = "ItemsConfig"

    private localeManager: LocaleManager
    
    private recursiveCloner: RecursiveCloner
    
    public priority: number = 2
    
    constructor(localeManager: LocaleManager)
    {
        super()

        this.localeManager = localeManager
    }
    
    protected override postDBInitialize(container: DependencyContainer): void 
    {
        super.postDBInitialize(container)

        this.recursiveCloner = container.resolve<RecursiveCloner>("RecursiveCloner")
    }

    protected override afterPostDB(): void 
    {
        for (let itemKey in this.config.items)
        {
            this.addItem(this.config.items[itemKey])
        }
    }

    private addItem(itemConfig: any)
    {
        let item: ITemplateItem = this.recursiveCloner.clone(this.databaseTables.templates.items[itemConfig.copyTemplateId]) as ITemplateItem
                
        this.setObjectProperties(item, itemConfig.changes)

        this.databaseTables.templates.items[item._id] = item

        if (itemConfig.slotId)
        {
            this.databaseTables.templates.items["55d7217a4bdc2d86028b456d"]._props.Slots
                .filter(s => s._name.includes(itemConfig.slotId))
                .forEach(s => s._props.filters.forEach(f => f.Filter.push(item._id)))
        }

        if (itemConfig.copyLocale == true)
        {
            this.localeManager.setENLocale(`${item._id} Name`, this.localeManager.getENLocale(`${itemConfig.copyTemplateId} Name`))
            this.localeManager.setENLocale(`${item._id} ShortName`, this.localeManager.getENLocale(`${itemConfig.copyTemplateId} ShortName`))
            this.localeManager.setENLocale(`${item._id} Description`, this.localeManager.getENLocale(`${itemConfig.copyTemplateId} Description`))
        }
    }

    private setObjectProperties(item: object, config: any)
    {
        for (let changeKey in config)
        {
            if (!(changeKey in item))
                continue

            if (Array.isArray(item[changeKey]) && 
                Array.isArray(config[changeKey]) &&
                item[changeKey].length == config[changeKey].length &&
                config[changeKey].length > 0)
            {
                config[changeKey].forEach((it, index) => 
                {
                    this.setObjectProperties(item[changeKey][index], config[changeKey][index])
                })

                continue
            }

            if (!Array.isArray(item[changeKey]) && 
                !Array.isArray(config[changeKey]) &&
                typeof item[changeKey] === "object" && 
                typeof config[changeKey] === "object")
            {
                this.setObjectProperties(item[changeKey], config[changeKey])

                continue
            }

            item[changeKey] = config[changeKey]
        }
    }
}