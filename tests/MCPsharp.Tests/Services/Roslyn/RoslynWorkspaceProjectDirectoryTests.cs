using MCPsharp.Services.Roslyn;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Roslyn;

/// <summary>
/// The project root a workspace was loaded from is decided when it loads, not from whatever is at the path later
/// </summary>
[TestFixture]
public class RoslynWorkspaceProjectDirectoryTests : FileServiceTestBase
{
    [Test]
    public async Task LoadedProjectDirectory_OfAFolderRenamedAfterwards_IsStillTheFolder()
    {
        // The auto-load reads its configuration from this directory, so it must not become the folder's parent
        var folder = CreateTestDirectory("project");
        CreateTestFile("public class Calc { }", ".cs", "project");
        using var workspace = new RoslynWorkspace();
        await workspace.InitializeAsync(folder);

        Directory.Move(folder, Path.Combine(TempDirectory, "renamed"));

        Assert.That(workspace.LoadedProjectDirectory, Is.EqualTo(Path.GetFullPath(folder)));
    }

    [Test]
    public void LoadedProjectDirectory_BeforeLoading_IsNull()
    {
        using var workspace = new RoslynWorkspace();

        Assert.That(workspace.LoadedProjectDirectory, Is.Null);
    }
}
