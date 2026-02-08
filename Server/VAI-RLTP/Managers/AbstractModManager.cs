using System.Text.Json.Nodes;
using SPTarkov.Server.Core.Models.Spt.Server;
using SPTarkov.Server.Core.Utils;

namespace VAI.RLTP.Managers;

public abstract class AbstractModManager
{
    protected abstract string ConfigName { get; }
    protected JsonNode? Config;

    protected bool PreSptInitialized;
    protected bool PostDbInitialized;
    protected bool PostSptInitialized;

    protected DatabaseTables DatabaseTables = null!;
    protected JsonUtil JsonUtil = null!;

    public virtual int Priority => 1;

    public void PreSptLoad()
    {
        EnsurePreSptInitialized();

        if (!IsEnabled())
        {
            return;
        }

        AfterPreSpt();
    }

    public void PostDbLoad()
    {
        EnsurePreSptInitialized();

        if (!IsEnabled())
        {
            return;
        }

        EnsurePostDbInitialized();
        AfterPostDb();
    }

    public void PostSptLoad()
    {
        EnsurePreSptInitialized();

        if (!IsEnabled())
        {
            return;
        }

        EnsurePostDbInitialized();
        EnsurePostSptInitialized();
        AfterPostSpt();
    }

    protected virtual void PreSptInitialize()
    {
        Config = LoadConfig(ConfigName);
        PreSptInitialized = true;
    }

    protected virtual void PostDbInitialize()
    {
        JsonUtil = ModContext.Current.JsonUtil;
        DatabaseTables = ModContext.Current.DatabaseServer.GetTables();
        PostDbInitialized = true;
    }

    protected virtual void PostSptInitialize()
    {
        PostSptInitialized = true;
    }

    protected virtual void AfterPreSpt()
    {
    }

    protected virtual void AfterPostDb()
    {
    }

    protected virtual void AfterPostSpt()
    {
    }

    protected bool GetConfigBool(string key, bool defaultValue = false)
    {
        if (Config is not JsonObject obj)
        {
            return defaultValue;
        }

        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
        {
            return defaultValue;
        }

        return node is JsonValue value && value.TryGetValue<bool>(out var result) ? result : defaultValue;
    }

    protected JsonArray? GetConfigArray(string key)
    {
        return Config is JsonObject obj && obj.TryGetPropertyValue(key, out var node)
            ? node as JsonArray
            : null;
    }

    protected JsonObject? GetConfigObject(string key)
    {
        return Config is JsonObject obj && obj.TryGetPropertyValue(key, out var node)
            ? node as JsonObject
            : null;
    }

    protected bool IsEnabled()
    {
        if (Config is null)
        {
            return false;
        }

        var enabledNode = Config["enabled"];
        return enabledNode is JsonValue value && value.TryGetValue<bool>(out var enabled) && enabled;
    }

    protected JsonNode? LoadConfig(string configName)
    {
        var path = Path.Combine(ModContext.Current.ConfigPath, $"{configName}.json");

        if (!File.Exists(path))
        {
            Constants.GetLogger().Warning($"{Constants.ModTitle}: Missing config {configName}.json");
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonNode.Parse(json);
    }

    private void EnsurePreSptInitialized()
    {
        if (!PreSptInitialized)
        {
            PreSptInitialize();
        }
    }

    private void EnsurePostDbInitialized()
    {
        if (!PostDbInitialized)
        {
            PostDbInitialize();
        }
    }

    private void EnsurePostSptInitialized()
    {
        if (!PostSptInitialized)
        {
            PostSptInitialize();
        }
    }
}
