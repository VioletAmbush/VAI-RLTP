import { ProfileHelper } from "@spt/helpers/ProfileHelper"
import { HashUtil } from "@spt/utils/HashUtil"
import { AbstractModManager } from "./AbstractModManager"
import { Constants } from "./Constants"
import { BodyPartHealth } from "@spt/models/eft/common/tables/IBotBase"

export class HealingManager extends AbstractModManager
{
    protected configName: string = "HealingConfig"

    public onPlayerDied(info: any, sessionId: string): void
    {
        if (this.config.enabled != true)
        {
            return
        }

        this.healPlayer(info, sessionId)

        Constants.getLogger().info(`${Constants.ModTitle}: Player healed!`)
    }

    private healPlayer(info: any, sessionId: string): void
    {
        const profile = Constants.Container.resolve<ProfileHelper>("ProfileHelper").getPmcProfile(sessionId)

        for (let partKey in profile.Health.BodyParts)
        {
            const part: BodyPartHealth = profile.Health.BodyParts[partKey]

            part.Health.Current = part.Health.Maximum

            part.Effects = null
        }
        
        if (profile.Stats.DamageHistory?.BodyParts)
        {
            for (let partKey in profile.Stats.DamageHistory.BodyParts)
            {
                profile.Stats.DamageHistory.BodyParts[partKey] = []
            }
        }
    }
}