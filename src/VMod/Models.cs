using System.Text.Json.Serialization;

namespace VMod;

internal enum InstallTarget
{
    Game,
    Server
}

internal enum ModState
{
    Installed,
    Disabled,
    New,
    NotInstalled
}

internal sealed class ModPackage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Version { get; set; } = "0.0.0";
    public string Description { get; set; } = "";
    public string? ResourceName { get; set; }
    public string? FilePath { get; set; }
    public bool IsComponent { get; set; }
    public bool IsBepInEx { get; set; }
    public bool InstallOnServer { get; set; } = true;
    public bool IsCustom { get; set; }
    public List<string> Dependencies { get; set; } = new();

    [JsonIgnore]
    public string VersionLabel => "v" + Version;
}

internal sealed class ModView
{
    public required ModPackage Package { get; init; }
    public required ModState State { get; init; }
    public required InstallTarget Target { get; init; }
}

internal sealed class AppSettings
{
    public string GamePath { get; set; } = "";
    public string ServerPath { get; set; } = "";
    public InstallTarget SelectedTarget { get; set; } = InstallTarget.Game;
}

internal sealed class PackageReceipt
{
    public string PackageId { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime InstalledAtUtc { get; set; }
    public bool Disabled { get; set; }
    public List<string> RelativePaths { get; set; } = new();
}

internal sealed class DashboardStats
{
    public int Installed { get; set; }
    public int Disabled { get; set; }
    public int New { get; set; }
}

internal sealed class OperationResult
{
    public int PackageCount { get; set; }
    public int FileCount { get; set; }
    public string? BackupPath { get; set; }
}

internal sealed class UpdateManifest
{
    public required Version Version { get; init; }
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public string Notes { get; init; } = "";
}
