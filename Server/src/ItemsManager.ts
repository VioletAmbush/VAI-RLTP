import { ITemplateItem } from "@spt/models/eft/common/tables/ITemplateItem";
import { AbstractModManager } from "./AbstractModManager";
import { Helper } from "./Helper";
import { RecursiveCloner } from "@spt/utils/cloners/RecursiveCloner"
import { DependencyContainer } from "tsyringe";


export class ItemsManager extends AbstractModManager
{
    protected configName: string = "ItemsConfig"

    private recursiveCloner: RecursiveCloner
    
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

        //this.logger.info(this.jsonUtil.serialize(item))
        //this.logger.info(this.jsonUtil.serialize(this.databaseTables.templates.items[itemConfig.copyTemplateId]))
    }

    private setObjectProperties(item: object, config: any)
    {
        for (let changeKey in config)
        {
            if (!(changeKey in item))
                continue

            if (typeof item[changeKey] === "object" && typeof config[changeKey] === "object")
            {
                this.setObjectProperties(item[changeKey], config[changeKey])

                continue
            }

            item[changeKey] = config[changeKey]
        }
    }
}