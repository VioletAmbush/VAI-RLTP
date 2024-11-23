import { DependencyContainer } from "tsyringe"

import { IPreSptLoadMod } from "@spt/models/external/IPreSptLoadMod"
import { IPostDBLoadMod } from "@spt/models/external/IPostDBLoadMod"
import { IPostSptLoadMod } from "@spt/models/external/IPostSptLoadMod"

import { AbstractModManager } from "./AbstractModManager";

import { GlobalsManager } from "./GlobalsManager"
import { Constants } from "./Constants"
import { WipeManager } from "./WipeManager"
import { DeathManager } from "./DeathManager"
import { StartManager } from "./StartManager"
import { HideoutManager } from "./HideoutManager"
import { TradersManager } from "./TradersManager"
import { PresetsManager } from "./PresetsManager"
import { ContainersManager } from "./ContainersManager"
import { QuestsManager } from "./QuestsManager"
import { WeaponsManager } from "./WeaponsManager"
import { HealingManager } from "./HealingManager"
import { ProfilesManager } from "./ProfilesManager"
import { ItemsManager } from "./ItemsManager";
import { LocaleManager } from "./LocaleManager";
import { BotsManager } from "./BotsManager";

export class ModManager implements IPreSptLoadMod, IPostDBLoadMod, IPostSptLoadMod
{
    private managers: AbstractModManager[] = []

    constructor()
    {
        const hideoutManager = new HideoutManager
        const wipeManager = new WipeManager
        const presetsManager = new PresetsManager
        const localeManager = new LocaleManager
        const profilesManager = new ProfilesManager(wipeManager, presetsManager, localeManager)
        const startManager = new StartManager(hideoutManager, profilesManager)
        const weaponsManager = new WeaponsManager
        const questsManager = new QuestsManager(presetsManager, weaponsManager, localeManager)
        const healingManager = new HealingManager

        this.managers.push(new GlobalsManager)
        this.managers.push(new ContainersManager)
        this.managers.push(new DeathManager(wipeManager, healingManager, startManager))
        this.managers.push(wipeManager)
        this.managers.push(startManager)
        this.managers.push(hideoutManager)
        this.managers.push(new TradersManager(presetsManager, questsManager))
        this.managers.push(presetsManager)
        this.managers.push(questsManager)
        this.managers.push(weaponsManager)
        this.managers.push(healingManager)
        this.managers.push(profilesManager)
        this.managers.push(new ItemsManager)
        this.managers.push(localeManager)
        this.managers.push(new BotsManager)

        this.managers = this.managers.sort((n1, n2) => n1.priority - n2.priority)
    }

    public preSptLoad(container: DependencyContainer): void
    {
        Constants.Container = container   

        this.managers.forEach(m => m.preSptLoad(container))
    }
  
    public postDBLoad(container: DependencyContainer): void 
    {
        Constants.Container = container   

        this.managers.forEach(m => m.postDBLoad(container))
    }

    public postSptLoad(container: DependencyContainer): void 
    {
        Constants.Container = container   

        this.managers.forEach(m => m.postSptLoad(container))
    }
}

module.exports = { mod: new ModManager() }