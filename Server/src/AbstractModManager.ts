import { DependencyContainer } from "tsyringe"

import { ILogger } from "@spt/models/spt/utils/ILogger"
import { JsonUtil } from "@spt/utils/JsonUtil"

import { IPreSptLoadMod } from "@spt/models/external/IPreSptLoadMod"
import { IPostDBLoadMod } from "@spt/models/external/IPostDBLoadMod"
import { IPostSptLoadMod } from "@spt/models/external/IPostSptLoadMod"
import { DatabaseServer } from "@spt/servers/DatabaseServer"
import { IDatabaseTables } from "@spt/models/spt/server/IDatabaseTables"

export abstract class AbstractModManager implements IPreSptLoadMod, IPostDBLoadMod, IPostSptLoadMod
{
    protected abstract configName: string
    protected config: any

    protected container: DependencyContainer
    protected jsonUtil: JsonUtil
    protected logger: ILogger

    protected databaseTables: IDatabaseTables

    protected preSptInitialized: boolean = false
    protected postDBInitialized: boolean = false
    protected postSptInitialized: boolean = false
    
    public priority: number = 1

    public preSptLoad(container: DependencyContainer): void
    {
        if (!this.preSptInitialized)
        {
            this.preSptInitialize(container)
        }

        if (this.config.enabled != true)
        {
            return
        }

        this.afterPreSpt()
    }

    public postDBLoad(container: DependencyContainer): void
    {
        if (!this.preSptInitialized)
        {
            this.preSptInitialize(container)
        }

        if (this.config.enabled != true)
        {
            return
        }

        if (!this.postDBInitialized)
        {
            this.postDBInitialize(container)
        }

        this.afterPostDB()
    }

    public postSptLoad(container: DependencyContainer): void 
    {
        if (!this.preSptInitialized)
        {
            this.preSptInitialize(container)
        }

        if (this.config.enabled != true)
        {
            return
        }

        if (!this.postDBInitialized)
        {
            this.postDBInitialize(container)
        }

        if (!this.postSptInitialized)
        {
            this.postSptInitialize(container)
        }

        this.afterPostSpt()
    }

    protected preSptInitialize(container: DependencyContainer): void
    {
        this.config = require(`../config/${this.configName}.json`)
        
        this.container = container

        this.preSptInitialized = true
    }

    protected postDBInitialize(container: DependencyContainer): void
    {
        this.container = container

        this.jsonUtil = this.container.resolve<JsonUtil>("JsonUtil")
        this.logger = this.container.resolve<ILogger>("WinstonLogger")

        const databaseServer = this.container.resolve<DatabaseServer>("DatabaseServer")
        this.databaseTables = databaseServer.getTables()

        this.postDBInitialized = true
    }

    protected postSptInitialize(container: DependencyContainer): void
    {

    }

    protected afterPreSpt(): void {}

    protected afterPostDB(): void {}

    protected afterPostSpt(): void {}
}