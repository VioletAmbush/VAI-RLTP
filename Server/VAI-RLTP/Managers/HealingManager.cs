using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class HealingManager : AbstractModManager
{
    protected override string ConfigName => "HealingConfig";

    public void OnPlayerDied(MongoId sessionId)
    {
        if (!IsEnabled())
        {
            return;
        }

        HealPlayer(sessionId);
        Constants.GetLogger().Info($"{Constants.ModTitle}: Player healed!");
    }

    private void HealPlayer(MongoId sessionId)
    {
        var profile = ModContext.Current.ProfileHelper.GetPmcProfile(sessionId);
        if (profile is null)
        {
            return;
        }

        var health = profile.Health;
        if (health?.BodyParts is null)
        {
            return;
        }

        foreach (var part in health.BodyParts.Values)
        {
            if (part?.Health != null)
            {
                part.Health.Current = part.Health.Maximum;
            }

            part.Effects = new Dictionary<string, BodyPartEffectProperties>();
        }
    }
}
