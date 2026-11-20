using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// Directories searched for analyzer assemblies are limited in DLL count and size, since every DLL found is
/// loaded. The directories here are generated; the real NuGet cache is never used.
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Analyzers")]
public class AnalyzerDirectoryScanLimitsTests : FileServiceTestBase
{
    private static readonly string[] ThreeFakeDlls = ["Fake0.dll", "Fake1.dll", "Fake2.dll"];

    /// <summary>
    /// Create <paramref name="count"/> placeholder DLLs of <paramref name="bytesEach"/> bytes under
    /// <paramref name="directory"/>, spread over package-like subdirectories
    /// </summary>
    internal static void CreateFakeDlls(string directory, int count, int bytesEach = 4)
    {
        for (var i = 0; i < count; i++)
        {
            var packageDirectory = Path.Combine(directory, $"package{i % 10}", "analyzers");
            Directory.CreateDirectory(packageDirectory);
            File.WriteAllBytes(Path.Combine(packageDirectory, $"Fake{i}.dll"), new byte[bytesEach]);
        }
    }

    /// <summary>
    /// A directory just over the DLL count limit, one of whose DLLs is a real analyzer assembly
    /// </summary>
    private string CreateDirectoryOverTheCountLimitWithAnAnalyzer(string name)
    {
        var directory = CreateTestDirectory(name);
        CreateFakeDlls(directory, AnalyzerDirectoryScanLimits.MaxAssemblyCount);
        File.Copy(typeof(StaticMethodAnalyzer).Assembly.Location, Path.Combine(directory, "MCPsharp.Analyzers.dll"));
        return directory;
    }

    [Test]
    public void Check_DirectoryWithinTheLimits_Passes()
    {
        var directory = CreateTestDirectory("small");
        CreateFakeDlls(directory, 3);

        Assert.That(AnalyzerDirectoryScanLimits.Check(directory), Is.Null);
    }

    [Test]
    public void ListAssemblies_DirectoryWithinTheLimits_ListsTheDllsItCounted()
    {
        // The search loads this list rather than looking again, so a directory that grows can't exceed the limits
        var directory = CreateTestDirectory("listed");
        CreateFakeDlls(directory, 3);
        File.WriteAllText(Path.Combine(directory, "readme.txt"), "not an assembly");

        var error = AnalyzerDirectoryScanLimits.ListAssemblies(directory, recursive: true, out var assemblies);

        Assert.That(error, Is.Null);
        Assert.That(assemblies.Select(Path.GetFileName), Is.EquivalentTo(ThreeFakeDlls));
    }

    [Test]
    public void ListAssemblies_DirectoryOverALimit_ListsNothing()
    {
        var directory = CreateTestDirectory("over");
        CreateFakeDlls(directory, AnalyzerDirectoryScanLimits.MaxAssemblyCount + 1);

        var error = AnalyzerDirectoryScanLimits.ListAssemblies(directory, recursive: true, out var assemblies);

        Assert.That(error, Does.Contain($"more than {AnalyzerDirectoryScanLimits.MaxAssemblyCount} DLLs"));
        Assert.That(assemblies, Is.Empty);
    }

    [Test]
    public void Check_MoreDllsThanTheLimit_ReturnsAnErrorNamingTheLimit()
    {
        var directory = CreateTestDirectory("many");
        CreateFakeDlls(directory, AnalyzerDirectoryScanLimits.MaxAssemblyCount + 1);

        var error = AnalyzerDirectoryScanLimits.Check(directory);

        Assert.That(error, Does.Contain($"more than {AnalyzerDirectoryScanLimits.MaxAssemblyCount} DLLs"));
        Assert.That(error, Does.Contain(directory));
        Assert.That(error, Does.Contain("analyzers/dotnet/cs"), "suggests a package's analyzer folder");
    }

    [Test]
    public void Check_DllsLargerThanTheSizeLimit_ReturnsAnErrorNamingTheLimit()
    {
        var directory = CreateTestDirectory("large");
        CreateFakeDlls(directory, 2, bytesEach: 1024);

        var error = AnalyzerDirectoryScanLimits.Check(directory, maxTotalBytes: 1024);

        Assert.That(error, Does.Contain("more than 1 KB of DLLs"));
        Assert.That(error, Does.Contain("analyzers/dotnet/cs"));
    }

    [Test]
    public void Check_NotRecursive_CountsOnlyTheDirectoryItself()
    {
        var directory = CreateTestDirectory("nested");
        CreateFakeDlls(directory, AnalyzerDirectoryScanLimits.MaxAssemblyCount + 1);

        Assert.That(AnalyzerDirectoryScanLimits.Check(directory, recursive: false), Is.Null);
    }

    [Test]
    public void RoslynAnalyzerLoader_DiscoverAnalyzerAssemblies_OverTheLimit_LoadsNothing()
    {
        var directory = CreateDirectoryOverTheCountLimitWithAnAnalyzer("analyzer-loader");
        var loader = new RoslynAnalyzerLoader(CreateNullLogger<RoslynAnalyzerLoader>(), CreateNullLoggerFactory());

        Assert.That(loader.DiscoverAnalyzerAssemblies(directory), Is.Empty);
    }

    [Test]
    public void RoslynCodeFixLoader_DiscoverCodeFixProviderAssemblies_OverTheLimit_LoadsNothing()
    {
        var directory = CreateTestDirectory("code-fix-loader");
        CreateFakeDlls(directory, AnalyzerDirectoryScanLimits.MaxAssemblyCount);
        File.Copy(typeof(StaticMethodFixer).Assembly.Location, Path.Combine(directory, "MCPsharp.Analyzers.CodeFixes.dll"));
        var loader = new RoslynCodeFixLoader(CreateNullLogger<RoslynCodeFixLoader>(), CreateNullLoggerFactory());

        Assert.That(loader.DiscoverCodeFixProviderAssemblies(directory), Is.Empty);
    }

    [Test]
    public async Task AnalyzerRegistry_DiscoverAnalyzersAsync_OverTheLimit_LoadsNothing()
    {
        // The registry only looks at the directory itself, so every DLL goes there, one of them an MCPsharp analyzer
        var directory = CreateTestDirectory("registry");
        for (var i = 0; i < AnalyzerDirectoryScanLimits.MaxAssemblyCount; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, $"Fake{i}.dll"), new byte[4]);
        }

        var probeName = $"RegistryProbe_{Guid.NewGuid():N}";
        AnalyzerAssemblyEmitter.Emit(probeName, Path.Combine(directory, probeName + ".dll"),
            "public sealed class RegistryProbeAnalyzer : MCPsharp.Services.Analyzers.BuiltIn.NUnitUpgraderAnalyzer { }");
        var registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());

        Assert.That(await registry.DiscoverAnalyzersAsync(directory), Is.Empty);
    }
}
