using MCPsharp.Models;

namespace MCPsharp.Services.Phase2;

/// <summary>
/// Mutable version of FeatureComponents for building up feature maps
/// </summary>
public class MutableFeatureComponents
{
    public string? Controller { get; set; }
    public string? Service { get; set; }
    public string? Repository { get; set; }
    public List<string> Models { get; set; } = new();
    public List<string> Migrations { get; set; } = new();
    public List<string> Config { get; set; } = new();
    public List<string> Tests { get; set; } = new();
    public string? Documentation { get; set; }
    public List<string> Workflows { get; set; } = new();

    /// <summary>
    /// Convert to immutable FeatureComponents
    /// </summary>
    public FeatureComponents ToImmutable()
    {
        return new FeatureComponents
        {
            Controller = Controller,
            Service = Service,
            Repository = Repository,
            Models = Models.Count > 0 ? Models : null,
            Migrations = Migrations.Count > 0 ? Migrations : null,
            Config = Config.Count > 0 ? Config : null,
            Tests = Tests.Count > 0 ? Tests : null,
            Documentation = Documentation,
            Workflows = Workflows.Count > 0 ? Workflows : null
        };
    }
}
