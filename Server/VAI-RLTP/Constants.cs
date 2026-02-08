using SPTarkov.Server.Core.Models.Spt.Server;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace VAI.RLTP;

public static class Constants
{
    public static bool MinimumPrices = false;
    public static bool PrintPresetsOnFleaEnter = false;
    public static bool PrintNewHashes = false;
    public static bool NoNewQuestsStartRequirements = false;
    public static bool NoQuestlockedItems = false;
    public static bool IgnoreMockQuestlocks = false;
    public static bool AllPresetsUnconditional = false;
    public static bool EasyQuests = false;
    public static bool PrintQuestCount = false;
    public static bool PrintPresetCount = false;
    public static bool PrintAmmoBatchDebug = false;
    public static bool InstantCrafting = false;
    public static bool EasyCrafting = false;

    public static string ModTitle = "VAI-RLTP";

    public static DatabaseTables GetDatabaseTables() => ModContext.Current.DatabaseServer.GetTables();

    public static JsonUtil GetJsonUtil() => ModContext.Current.JsonUtil;

    public static HashUtil GetHashUtil() => ModContext.Current.HashUtil;

    public static RandomUtil GetRandomUtil() => ModContext.Current.RandomUtil;

    public static ISptLogger<ModContext> GetLogger() => ModContext.Current.Logger;
}
