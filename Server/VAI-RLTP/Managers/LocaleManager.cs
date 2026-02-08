using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Services;

namespace VAI.RLTP.Managers;

[Injectable(InjectionType.Singleton)]
public sealed class LocaleManager(DatabaseService databaseService) : AbstractModManager
{
    private readonly DatabaseService _databaseService = databaseService;
    private Dictionary<string, string>? _enLocaleTable;
    private readonly Dictionary<string, string> _globalOverridesAll = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _globalOverridesByLang = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _globalTransformers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _serverOverridesAll = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _serverOverridesByLang = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _serverTransformers = new(StringComparer.OrdinalIgnoreCase);

    protected override string ConfigName => "LocaleConfig";

    protected override void AfterPostDb()
    {
        var languages = GetConfigObject("languages");
        if (languages != null && languages.Count > 0)
        {
            foreach (var (langKey, langNode) in languages)
            {
                if (langNode is not JsonObject langLocales)
                {
                    continue;
                }

                var langDict = GetOrCreateLangOverrides(_globalOverridesByLang, langKey);
                foreach (var (localeKey, localeValue) in langLocales)
                {
                    if (localeValue is null)
                    {
                        continue;
                    }

                    langDict[localeKey] = localeValue.ToString();
                }
            }
        }
        EnsureGlobalTransformers();
        EnsureServerTransformers();
    }

    public void SetENLocale(string id, string value)
    {
        _globalOverridesAll[id] = value;
        if (_enLocaleTable != null)
        {
            _enLocaleTable[id] = value;
        }

        EnsureGlobalTransformers();
    }

    public void SetENLocaleOnly(string id, string value)
    {
        var langOverrides = GetOrCreateLangOverrides(_globalOverridesByLang, "en");
        langOverrides[id] = value;
        if (_enLocaleTable != null)
        {
            _enLocaleTable[id] = value;
        }

        EnsureGlobalTransformers();
    }

    public void SetENServerLocale(string id, string value)
    {
        _serverOverridesAll[id] = value;
        EnsureServerTransformers();
    }

    public string GetENLocale(string id)
    {
        return TryGetENLocaleValue(id, out var value)
            ? value
            : $"UNKNOWN LOCALE ID {id}";
    }

    public bool TryGetENLocale(string id, out string value)
    {
        return TryGetENLocaleValue(id, out value);
    }

    public Dictionary<string, string>? GetENLocaleTable()
    {
        if (_enLocaleTable != null)
        {
            return _enLocaleTable;
        }

        EnsureGlobalTransformers();
        var locales = _databaseService.GetLocales();
        if (!locales.Global.TryGetValue("en", out var enLazy))
        {
            return null;
        }

        _enLocaleTable = enLazy.Value;
        return _enLocaleTable;
    }

    public bool TryGetLocaleValue(Dictionary<string, string>? table, string key, out string value)
    {
        value = string.Empty;
        if (table is null)
        {
            return false;
        }

        if (!table.TryGetValue(key, out var existing))
        {
            return false;
        }

        value = existing ?? string.Empty;
        return value.Length > 0;
    }

    public bool TryGetENLocaleValue(string key, out string value)
    {
        var table = GetENLocaleTable();
        return TryGetLocaleValue(table, key, out value);
    }

    private void EnsureGlobalTransformers()
    {
        var locales = _databaseService.GetLocales();
        foreach (var (langKey, lazy) in locales.Global)
        {
            if (!_globalTransformers.Add(langKey))
            {
                continue;
            }

            var capturedLang = langKey;
            lazy.AddTransformer(localeData =>
            {
                ApplyOverrides(localeData, _globalOverridesAll);
                if (_globalOverridesByLang.TryGetValue(capturedLang, out var perLang))
                {
                    ApplyOverrides(localeData, perLang);
                }

                return localeData;
            });
        }
    }

    private void EnsureServerTransformers()
    {
        var locales = _databaseService.GetLocales();
        var serverLocales = LocaleServerAccessor.GetOrNormalizeServerLocales(locales, nameof(LocaleManager));
        if (serverLocales is null)
        {
            return;
        }

        foreach (var (langKey, lazy) in serverLocales)
        {
            if (!_serverTransformers.Add(langKey))
            {
                continue;
            }

            var capturedLang = langKey;
            lazy.AddTransformer(localeData =>
            {
                ApplyOverrides(localeData, _serverOverridesAll);
                if (_serverOverridesByLang.TryGetValue(capturedLang, out var perLang))
                {
                    ApplyOverrides(localeData, perLang);
                }

                return localeData;
            });
        }
    }

    private static void ApplyOverrides(Dictionary<string, string>? localeData, Dictionary<string, string> overrides)
    {
        if (localeData is null || overrides.Count == 0)
        {
            return;
        }

        foreach (var (key, value) in overrides)
        {
            localeData[key] = value;
        }
    }

    private static Dictionary<string, string> GetOrCreateLangOverrides(
        Dictionary<string, Dictionary<string, string>> table,
        string langKey)
    {
        if (!table.TryGetValue(langKey, out var overrides))
        {
            overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            table[langKey] = overrides;
        }

        return overrides;
    }

}
