import { IPmcData } from "@spt/models/eft/common/IPmcData"
import { Item } from "@spt/models/eft/common/tables/IItem"
import { ITemplateItem } from "@spt/models/eft/common/tables/ITemplateItem"
import { IDatabaseTables } from "@spt/models/spt/server/IDatabaseTables"
import { Location } from "@spt/models/eft/common/tables/IItem"
import { ConfigMapper } from "./ConfigMapper"
import { Constants } from "./Constants"


export class Helper 
{
    public static setItemBuffs(databaseTables: IDatabaseTables, item: ITemplateItem, buffsConfig: any): void
    {
        const buffName = `Buff${item._id}`

        databaseTables.globals.config.Health.Effects.Stimulator.Buffs[buffName] = ConfigMapper.mapBuffs(buffsConfig)

        item._props.StimulatorBuffs = buffName
    }

    public static trySetItemRarity(item: ITemplateItem, itemConfig: any): void
    {
        if (!itemConfig.rarity)
        {
            return
        }

        item._props.BackgroundColor = ConfigMapper.mapRarity(itemConfig.rarity)
    }

    public static iterateConfigItems(
        databaseTables: IDatabaseTables,
        config: any,
        action: (item: ITemplateItem, itemConfig: any, databaseTables: IDatabaseTables, config: any) => void = null,
        trySetRarity: boolean = true)
    {
        for (let itemKey in config.items)
        {
            const itemConfig = config.items[itemKey]
            const item = databaseTables.templates.items[itemConfig.id]

            if (!item)
            {
                Constants.getLogger().error(`${Constants.ModTitle}: Item with id ${itemConfig.id} not found!`)
                continue
            }

            if (trySetRarity)
            {
                Helper.trySetItemRarity(item, itemConfig)
            }

            if (action != null)
            {
                action(item, itemConfig, databaseTables, config)
            }
        }
    }

    public static getItemTree(inventory: Item[], root: Item): Item[]
    {
        let result: Item[] = [ root ] 

        while (true)
        {
            const count = result.length

            inventory.forEach(item => {

                if (!result.includes(item) && result.find(resItem => item.parentId == resItem._id))
                {
                    result.push(item)
                }
            })

            if (count == result.length)
            {
                break
            }
        }

        return result
    }

    public static isStackable(templateId: string) : boolean
    {
        let item = Constants.getDatabaseTables().templates.items[templateId]

        if (!item)
            return false;

        return item._props.StackMaxSize && item._props.StackMaxSize > 1
    }

    public static isMoney(templateId: string) : boolean
    {
        return [ 
            "5449016a4bdc2d6f028b456f", 
            "5696686a4bdc2da3298b456a", 
            "569668774bdc2da2298b4568",
        ].includes(templateId)
    }

    public static getStashSize(profile: IPmcData): number
    {
        var stashBonusCount = profile.Bonuses.filter(b => b.type === "StashSize").length

        switch (stashBonusCount)
        {
            case 1: return 30
            case 2: return 40
            case 3: return 50
            case 4: return 70
            default: return 70
        }
    }

    public static fillLocations(profile: IPmcData, databaseTables: IDatabaseTables)
    {
        const stashMap: Array<[ boolean, boolean, boolean, boolean, boolean, boolean, boolean, boolean, boolean, boolean ]> = []
        const stashSize = this.getStashSize(profile)

        for (let r = 0; r < stashSize; r++)
        {
            stashMap.push([ false, false, false, false, false, false, false, false, false, false ])
        }

        const stashItems = profile.Inventory.items.filter(item => item.parentId == profile.Inventory.stash);

        stashItems.forEach(item => 
        {
            const itemTemplate = databaseTables.templates.items[item._tpl]

            if (item.location as Location)
            {
                const location = item.location as Location

                this.iterateRect(location.x, location.y, itemTemplate._props.Width, itemTemplate._props.Height, location.r == "Vertical", stashMap, (x, y) => 
                {
                    stashMap[y][x] = true
                })

                return
            }
        })

        //this.printMap(stashMap, stashSize)

        const unplacedItems: Item[] = []

        stashItems.forEach(item => 
        {
            const itemTemplate = databaseTables.templates.items[item._tpl]

            if (!item.location)
            {
                const location = this.findItemLocation(itemTemplate, false, stashMap)

                if (location == null)
                {
                    Constants.getLogger().error(`${Constants.ModTitle}: Error - could not place item in stash`)
                    unplacedItems.push(item)
                    return 
                }

                item.location = location

                this.iterateRect(location.x, location.y, itemTemplate._props.Width, itemTemplate._props.Height, false, stashMap, (x, y) => 
                {
                    stashMap[y][x] = true
                })
            }
        })

        //this.printMap(stashMap, stashSize)
    }

    public static getTradersRecords(val: number) : Record<string, number>
    {
        let res: Record<string, number> = {}

        res[this.acronymToTraderId("p")] = val
        res[this.acronymToTraderId("t")] = val
        res[this.acronymToTraderId("f")] = val
        res[this.acronymToTraderId("s")] = val
        res[this.acronymToTraderId("pk")] = val
        res[this.acronymToTraderId("m")] = val
        res[this.acronymToTraderId("r")] = val
        res[this.acronymToTraderId("j")] = val

        return res
    }

    public static acronymToTraderId(acr: string) : string
    {
        switch (acr)
        {
            case "p": return "54cb50c76803fa8b248b4571" // prapor
            case "t": return "54cb57776803fa99248b456e" // the_rapist
            case "f": return "579dc571d53a0658a154fbec" // fence
            case "s": return "58330581ace78e27b8b10cee" // skier
            case "pk": return "5935c25fb3acc3127c3d8cd9" // piss keeper
            case "m": return "5a7c2eca46aef81a7ca2145d" // mechanicus
            case "r": return "5ac3b934156ae10c4430e83c" // ragman
            case "j": return "5c0647fdd443bc2504c2d371" // dumbass
            case "re": return "6617beeaa9cfa777ca915b7c" // useless
        }
    }

    public static scavTypeToString(scavType: string) : string
    {
        switch (scavType)
        {
            // Reshala
            case "bossBully": return "Reshala"
            case "followerBully": return "Reshala Guard"

            // Tagilla
            case "bossTagilla": return "Tagilla"

            // Shturman
            case "bossKojaniy": return "Shturman"
            case "followerKojaniy": return "Shturman Guard"

            // Sanitar
            case "bossSanitar": return "Sanitar"
            case "followerSanitar": return "Sanitar Guard"
            
            // Killa
            case "bossKilla": return "Killa"

            // Kaban
            case "bossBoar": return "Kaban"
            case "bossBoarSniper": return "Kaban Sniper"
            case "followerBoar": return "Kaban Guard"

            // Rogue
            case "exUsec": return "Rogue"
            // Goons
            case "bossKnight": return "Knight"
            case "followerBirdEye": return "Bird Eye"
            case "followerBigPipe": return "Big Pipe"

            // Glukhar
            case "bossGluhar": return "Glukhar"
            case "followerGluharAssault": return "Glukhar Assault"
            case "followerGluharScout": return "Glukhar Scout"
            case "followerGluharSecurity": return "Glukhar Security"
            case "followerGluharSnipe": return "Glukhar Sniper"

            // Cultists
            case "sectantPriest": return "Cultist Priest"
            case "sectantWarrior": return "Cultist Warrior"

            case "marksman": return "Scav Sniper"
            
            case "pmcBot": return "Raider"

            case "pmcUSEC": return "USEC PMC"
            case "pmcBEAR": return "BEAR PMC"



            default: return `UNKNOWN SCAV TYPE (${scavType})`
        }
    }

    private static findItemLocation(item: ITemplateItem, rotated: boolean, stashMap: any): Location | null
    {
        for (let r = 0; r < stashMap.length; r++)
        {
            for (let c = 0; c < stashMap[r].length; c++)
            {
                if (stashMap[r][c] == true ||
                    (item._props.Width > stashMap[r].length - c && !rotated) ||
                    (item._props.Height > stashMap[r].length - c && rotated))
                {
                    continue
                }

                let foundPlace = true

                this.iterateRect(c, r, item._props.Width, item._props.Height, rotated, stashMap, (x, y) => 
                {
                    if (stashMap[y][x] == true)
                    {
                        foundPlace = false
                    }
                })

                if (!foundPlace)
                    continue

                return {
                    x: c,
                    y: r,
                    r: rotated ? "Vertical" : "Horizontal",
                    isSearched: true
                }
                
            }
        }

        return null
    }

    private static iterateRect(x: number, y: number, width: number, height: number, r: boolean, stashMap: any, callback: (x: number, y: number) => void)
    {
        for (let i = 0; i < width * height; i++)
        {
            if (r == false)
            {
                callback(x + i % width, y + Math.floor(i / width))
            }
            else
            {
                callback(x + i % height, y + Math.floor(i / height))
            }
        }
    }

    private static printMap(stashMap: any, stashSize: number)
    {
        console.log("[BEGIN_____]")

        for (let r = 0; r < stashSize; r++)
        {
            console.log(`[${stashMap[r].map(slot => slot == true ? "X" : ".").join("")}]`)
        }

        console.log("[END_______]")
    }
} 