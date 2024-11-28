import { AbstractModManager } from "./AbstractModManager";


export class LocaleManager extends AbstractModManager 
{
    protected configName: string = "LocaleConfig"

    protected afterPostDB(): void 
    {
        for (let langKey in this.config.languages)
        {
            for (let localeKey in this.config.languages[langKey])
            {
                this.databaseTables.locales.global[langKey][localeKey] = this.config.languages[langKey][localeKey]
            }
        }
    }

    public setENLocale(id: string, value: string)
    {
        for (let langKey in this.databaseTables.locales.global)
        {
            this.databaseTables.locales.global[langKey][id] = value
        }
    }

    public setENServerLocale(id: string, value: string)
    {
        for (let langKey in this.databaseTables.locales.server)
        {
            this.databaseTables.locales.server[langKey][id] = value
        }
    }

    public getENLocale(id: string): string
    {
        return this.databaseTables.locales.global["en"][id] ?? `UNKNOWN LOCALE ID ${id}`
    }
}