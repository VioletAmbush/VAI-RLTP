import { IBuff } from "@spt/models/eft/common/IGlobals"

export class ConfigMapper 
{
    public static mapBuffs(config: any): IBuff[]
    {
        const buffs: IBuff[] = []

        for (let key in config)
        {
            buffs.push(this.mapBuff(config[key]))
        }

        return buffs
    }

    public static mapBuff(config: any): IBuff
    {
        return {
            BuffType: config.buffType,
            Chance: config.chance !== undefined ? config.chance : 1,
            Delay: config.delay !== undefined ? config.delay : 1,
            Duration: config.duration !== undefined ? config.duration : 60,
            Value: config.value !== undefined ? config.value : 0,
            AbsoluteValue: config.absoluteValue !== undefined ? config.absoluteValue : true,
            SkillName: config.skillName !== undefined ? config.skillName : ""
        }
    }

    public static mapRarity(rarity: string): string 
    {
        switch(rarity)
        {
            case "1": 
            case "c": 
            case "common": 
                return "grey"
            case "2": 
            case "uc": 
            case "uncommon": 
                return "green"
            case "3": 
            case "e": 
            case "epic": 
                return "violet"
            case "4": 
            case "r": 
            case "rare": 
                return "yellow"
            case "5": 
            case "l": 
            case "legendary": 
                return "red"
            case "6": 
            case "u": 
            case "unique": 
                return "orange"
            case "7": 
            case "q": 
            case "quest": 
                return "blue"
            default: return "grey"
        }
    }
} 