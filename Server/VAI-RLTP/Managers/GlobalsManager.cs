using System;
using SPTarkov.DI.Annotations;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class GlobalsManager : AbstractModManager
{
    protected override string ConfigName => "GlobalsConfig";

    protected override void AfterPostDb()
    {
        if (Constants.PrintNewHashes)
        {
            TryPrintHashes();
        }

        if (GetConfigBool("removeStatusEffectRemovePrices"))
        {
            var effects = DatabaseTables.Globals?.Configuration?.Health?.Effects;
            if (effects != null)
            {
                effects.BreakPart.RemovePrice = 0;
                effects.Fracture.RemovePrice = 0;
                effects.LightBleeding.RemovePrice = 0;
                effects.HeavyBleeding.RemovePrice = 0;
                effects.Intoxication.RemovePrice = 0;
            }
        }

        if (GetConfigBool("removePostRaidHeal"))
        {
            foreach (var trader in DatabaseTables.Traders.Values)
            {
                trader.Base.Medic = false;
            }
        }

        if (GetConfigBool("disableFlea"))
        {
            var ragfair = DatabaseTables.Globals?.Configuration?.RagFair;
            if (ragfair != null)
            {
                ragfair.Enabled = true;
                ragfair.MinUserLevel = 70;
            }
        }

        if (GetConfigBool("disableFleaBlacklist"))
        {
            foreach (var item in DatabaseTables.Templates.Items.Values)
            {
                if (item.Properties == null)
                {
                    continue;
                }

                item.Properties.CanSellOnRagfair = true;
                item.Properties.CanRequireOnRagfair = true;
            }
        }

        if (GetConfigBool("removeTraderMoneyRequirements"))
        {
            foreach (var trader in DatabaseTables.Traders.Values)
            {
                if (trader.Base?.LoyaltyLevels == null)
                {
                    continue;
                }

                foreach (var level in trader.Base.LoyaltyLevels)
                {
                    level.MinSalesSum = 0;
                }
            }
        }

        if (GetConfigBool("delayScavRun"))
        {
            if (DatabaseTables.Globals?.Configuration != null)
            {
                DatabaseTables.Globals.Configuration.SavagePlayCooldown = 2147483646;
            }
        }

        Constants.GetLogger().Info($"{Constants.ModTitle}: Globals changes applied!");
    }

    private static void TryPrintHashes()
    {
        try
        {
            for (var i = 0; i < 100; i++)
            {
                var value = Helper.GenerateSha256Id(Guid.NewGuid().ToString("N"));
                Console.WriteLine(value);
            }
        }
        catch (Exception ex)
        {
            Constants.GetLogger().Warning($"{Constants.ModTitle}: Failed to print hashes. {ex.Message}");
        }
    }
}
