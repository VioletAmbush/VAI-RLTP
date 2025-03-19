import { ITemplateItem } from "@spt/models/eft/common/tables/ITemplateItem";
import { AbstractModManager } from "./AbstractModManager";
import { Helper } from "./Helper";
import { RecursiveCloner } from "@spt/utils/cloners/RecursiveCloner"
import { DependencyContainer } from "tsyringe";
import { LocaleManager } from "./LocaleManager";
import { Constants } from "./Constants";


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
        //console.log(`M60E6: ${this.jsonUtil.serialize(this.databaseTables.templates.items["661ceb1b9311543c7104149b"])}`)
        //console.log(`Hanson 13.7 barrel: ${this.jsonUtil.serialize(this.databaseTables.templates.items["63d3ce0446bd475bcb50f55f"])}`)

        for (let itemKey in this.config.items)
        {
            this.addItem(itemKey, this.config.items[itemKey])
        }

        if (this.config.addUBGLCompat == true)
        {
            [ 
                "59e6152586f77473dc057aa1", 
                "67495c74dfe62c2d7400002a", 
                "59e6687d86f77411d949b251", 
                "67495c74dfe62c2d74000029", 
                "5ac66cb05acfc40198510a10", 
                "5ac66d2e5acfc43b321d4b53", 
                "6499849fc93611967b034949", 
                "67495c74dfe62c2d74000045", 
                "5bf3e03b0db834001d2c4a9c", 
                "67495c74dfe62c2d74000032", 
                "5644bd2b4bdc2d3b4c8b4572", 
                "5ac4cd105acfc40016339859", 
                "5bf3e0490db83400196199af", 
                "5ab8e9fcd8ce870019439434", 
                "59d6088586f774275f37482f", 
                "67474dd2a7f5b436b8000025", 
                "67495c74dfe62c2d7400003d", 
                "59ff346386f77477562ff5e2", 
                "5abcbc27d8ce8700182eceeb",
                "5a0ec13bfcdbcb00165aa685"
            ].forEach(id => this.databaseTables.templates.items[id]._props.Slots
                .find(s => s._name == "mod_launcher")._props.filters
                .forEach(f => 
                {
                    if (!("67495c74dfe62c2d7400002d" in f.Filter))
                        f.Filter.push("67495c74dfe62c2d7400002d"); 
                }));

            [ 
                "55d3632e4bdc2d972f8b4569", 
                "63d3ce0446bd475bcb50f55f" 
            ].forEach(id => this.databaseTables.templates.items[id]._props.Slots
                .find(s => s._name == "mod_launcher")._props.filters
                .forEach(f => 
                { 
                    if (!("67495c74dfe62c2d7400002c" in f.Filter))
                        f.Filter.push("67495c74dfe62c2d7400002c");
                }));
                
        }

        if (this.config.add366TKMBubenCompat == true)
        {
            this.databaseTables.templates.items["6513f0a194c72326990a3868"]._props.Cartridges
                .find(c => c._name == "cartridges")._props.filters
                .forEach(f => f.Filter.push(...[
                    "59e655cb86f77411dc52a77b",
                    "59e6542b86f77411dc52a77a",
                    "59e6658b86f77411d949b250",
                    "5f0596629e22f464da6bbdd9"
                ]))
        }

        if (this.config.add6851T5000Compat == true)
        {
            this.databaseTables.templates.items["5df25b6c0b92095fd441e4cf"]._props.Cartridges
                .find(c => c._name == "cartridges")._props.filters
                .forEach(f => f.Filter.push(...[
                    "6529302b8c26af6326029fb7",
                    "6529243824cbe3c74a05e5c1"
                ]))
        }
    }

    private addItem(itemName: string, itemConfig: any)
    {
        if (!itemConfig.changes._id ||
            itemConfig.changes._id.length == 0)
        {
            
            this.logger.warning(`${Constants.ModTitle}: Empty id in item ${itemName}`)
            return
        }

        let item: ITemplateItem = this.jsonUtil.deserialize<ITemplateItem>(
            this.jsonUtil.serialize(
                this.databaseTables.templates.items[itemConfig.copyTemplateId]).replaceAll(itemConfig.copyTemplateId, itemConfig.changes._id))
                
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
            if (itemConfig.modTitle &&
                itemConfig.modTitle.length > 0)
            {
                this.localeManager.setENLocale(`${item._id} Name`, `${this.localeManager.getENLocale(`${itemConfig.copyTemplateId} Name`)} ${itemConfig.modTitle}`)
                this.localeManager.setENLocale(`${item._id} Description`, `${this.localeManager.getENLocale(`${itemConfig.copyTemplateId} Description`)} ${itemConfig.modTitle}`)
            }
            else
            {
                this.localeManager.setENLocale(`${item._id} Name`, this.localeManager.getENLocale(`${itemConfig.copyTemplateId} Name`))
                this.localeManager.setENLocale(`${item._id} Description`, this.localeManager.getENLocale(`${itemConfig.copyTemplateId} Description`))
            }

            this.localeManager.setENLocale(`${item._id} ShortName`, `${this.localeManager.getENLocale(`${itemConfig.copyTemplateId} ShortName`)}`)
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
                if (!Array.isArray(item[changeKey][0]) &&
                    typeof(item[changeKey][0]) !== "object")
                {
                    item[changeKey] = config[changeKey]

                    continue
                }

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

            if (config[changeKey].length == 0)
                return

            item[changeKey] = config[changeKey]
        }
    }
}