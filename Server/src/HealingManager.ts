import { ProfileHelper } from "@spt/helpers/ProfileHelper"
import { AbstractModManager } from "./AbstractModManager"
import { Constants } from "./Constants"
import { IBodyPartHealth } from "@spt/models/eft/common/tables/IBotBase"

export class HealingManager extends AbstractModManager
{
    protected configName: string = "HealingConfig"

    public onPlayerDied(sessionId: string): void
    {
        if (this.config.enabled != true)
        {
            return
        }

        this.healPlayer(sessionId)

        Constants.getLogger().info(`${Constants.ModTitle}: Player healed!`)
    }

    private healPlayer(sessionId: string): void
    {
        const profile = Constants.Container.resolve<ProfileHelper>("ProfileHelper").getPmcProfile(sessionId)

        for (let partKey in profile.Health.BodyParts)
        {
            const part: IBodyPartHealth = profile.Health.BodyParts[partKey]

            part.Health.Current = part.Health.Maximum

            part.Effects = {}
        }
    }
}