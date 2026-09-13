using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;

namespace VAI.RLTP;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.vai.rltp";
    public string Name { get; init; } = "VAI-RLTP";
    public string Author { get; init; } = "VioletAmbush, Incurso";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.2.3");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "CC BY-NC-SA 3.0";
}
