using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;

namespace VAI.RLTP;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.vai.rltp";
    public override string Name { get; init; } = "VAI-RLTP";
    public override string Author { get; init; } = "VioletAmbush + Incurso";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.2.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");

    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "CC BY-NC-SA 3.0";
}
