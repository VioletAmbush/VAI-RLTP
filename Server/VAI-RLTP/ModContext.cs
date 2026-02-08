using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace VAI.RLTP;

[Injectable(InjectionType.Singleton)]
public sealed class ModContext
{
    public static ModContext Current { get; private set; } = null!;

    public ModHelper ModHelper { get; }
    public ProfileHelper ProfileHelper { get; }
    public DatabaseServer DatabaseServer { get; }
    public ConfigServer ConfigServer { get; }
    public JsonUtil JsonUtil { get; }
    public HashUtil HashUtil { get; }
    public RandomUtil RandomUtil { get; }
    public ISptLogger<ModContext> Logger { get; }

    public string ModPath { get; }
    public string ConfigPath { get; }
    public Dictionary<string, (int StackCount, int BuyRestriction)> AssortOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ModContext(
        ModHelper modHelper,
        DatabaseServer databaseServer,
        ConfigServer configServer,
        JsonUtil jsonUtil,
        HashUtil hashUtil,
        RandomUtil randomUtil,
        ISptLogger<ModContext> logger,
        ProfileHelper profileHelper)
    {
        ModHelper = modHelper;
        ProfileHelper = profileHelper;
        DatabaseServer = databaseServer;
        ConfigServer = configServer;
        JsonUtil = jsonUtil;
        HashUtil = hashUtil;
        RandomUtil = randomUtil;
        Logger = logger;

        ModPath = modHelper.GetAbsolutePathToModFolder(typeof(ModContext).Assembly);
        ConfigPath = Path.Combine(ModPath, "config");

        Current = this;
    }
}
