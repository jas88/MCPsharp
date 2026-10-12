using MCPsharp.Models.Consolidated;
using MCPsharp.Services;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Consolidated;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Containment checks must not be fooled by sibling directories sharing a name prefix
/// (<c>/root2</c> vs <c>/root</c>) or by <c>..</c> traversal.
/// </summary>
[TestFixture]
public class PathContainmentTests
{
    private static readonly bool CaseInsensitivePlatform = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    private string _parent = null!;
    private string _root = null!;
    private string _sibling = null!;

    [SetUp]
    public void SetUp()
    {
        _parent = Path.Combine(Path.GetTempPath(), $"mcpsharp_containment_{Guid.NewGuid():N}");
        _root = Path.Combine(_parent, "root");
        _sibling = Path.Combine(_parent, "root2");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_sibling);
        File.WriteAllText(Path.Combine(_root, "inside.txt"), "inside");
        File.WriteAllText(Path.Combine(_sibling, "secret.txt"), "secret");
        File.WriteAllText(Path.Combine(_parent, "parent.txt"), "parent");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_parent))
        {
            Directory.Delete(_parent, true);
        }
    }

    #region PathContainment

    [Test]
    public void IsWithin_RootItself_IsContained()
    {
        Assert.That(PathContainment.IsWithin(_root, _root), Is.True);
        Assert.That(PathContainment.IsWithin(_root, _root + Path.DirectorySeparatorChar), Is.True);
        Assert.That(PathContainment.IsWithin(_root + Path.DirectorySeparatorChar, _root), Is.True);
    }

    [Test]
    public void IsWithin_Descendants_AreContained()
    {
        Assert.That(PathContainment.IsWithin(_root, Path.Combine(_root, "inside.txt")), Is.True);
        Assert.That(PathContainment.IsWithin(_root, Path.Combine(_root, "a", "b", "c.cs")), Is.True);
        Assert.That(PathContainment.IsWithin(_root, Path.Combine(_root, "a", "..", "c.cs")), Is.True);
    }

    [Test]
    public void IsWithin_SiblingWithSharedPrefix_IsNotContained()
    {
        Assert.That(PathContainment.IsWithin(_root, _sibling), Is.False);
        Assert.That(PathContainment.IsWithin(_root, Path.Combine(_sibling, "secret.txt")), Is.False);
        Assert.That(PathContainment.IsWithin(_root, _root + "-other"), Is.False);
    }

    [Test]
    public void IsWithin_DotDotTraversal_IsNotContained()
    {
        Assert.That(PathContainment.IsWithin(_root, Path.Combine(_root, "..", "parent.txt")), Is.False);
        Assert.That(PathContainment.IsWithin(_root, Path.Combine(_root, "..", "root2", "secret.txt")), Is.False);
        Assert.That(PathContainment.IsWithin(_root, Path.Combine(_root, "a", "..", "..", "parent.txt")), Is.False);
        Assert.That(PathContainment.IsWithin(_root, Path.Combine(_root, "..")), Is.False);
    }

    [Test]
    public void IsWithin_EmptyCandidate_IsNotContained()
    {
        Assert.That(PathContainment.IsWithin(_root, string.Empty), Is.False);
    }

    [Test]
    public void IsWithin_FileSystemRoot_ContainsEverything()
    {
        var fileSystemRoot = Path.GetPathRoot(_root)!;

        Assert.That(PathContainment.IsWithin(fileSystemRoot, _root), Is.True);
        Assert.That(PathContainment.IsWithin(fileSystemRoot, fileSystemRoot), Is.True);
    }

    [Test]
    public void IsWithin_DifferentCase_FollowsPlatformCaseSensitivity()
    {
        var upperCased = Path.Combine(_root.ToUpperInvariant(), "inside.txt");

        Assert.That(PathContainment.IsWithin(_root, upperCased), Is.EqualTo(CaseInsensitivePlatform));
    }

    [Test]
    public void TryResolveWithin_ResolvesRelativePathsAgainstRoot()
    {
        Assert.That(PathContainment.TryResolveWithin(_root, "inside.txt", out var inside), Is.True);
        Assert.That(inside, Is.EqualTo(Path.Combine(_root, "inside.txt")));

        Assert.That(PathContainment.TryResolveWithin(_root, "../root2/secret.txt", out var sibling), Is.False);
        Assert.That(sibling, Is.EqualTo(Path.Combine(_sibling, "secret.txt")));

        Assert.That(PathContainment.TryResolveWithin(_root, Path.Combine(_sibling, "secret.txt"), out _), Is.False);
        Assert.That(PathContainment.TryResolveWithin(_root, string.Empty, out _), Is.False);
    }

    #endregion

    #region Symbolic links

    [Test]
    public void IsWithin_DirectoryLinkPointingOutside_IsNotContained()
    {
        var link = CreateDirectoryLinkOrIgnore(Path.Combine(_root, "escape"), _sibling);

        Assert.Multiple(() =>
        {
            Assert.That(PathContainment.IsWithin(_root, link), Is.False);
            Assert.That(PathContainment.IsWithin(_root, Path.Combine(link, "secret.txt")), Is.False);
            Assert.That(PathContainment.IsWithin(_root, Path.Combine(link, "new", "file.txt")), Is.False,
                "a file to be created beneath an escaping link is outside the root");
            Assert.That(PathContainment.TryResolveWithin(_root, "escape/secret.txt", out _), Is.False);
        });
    }

    [Test]
    public void IsWithin_FileLinkPointingOutside_IsNotContained()
    {
        var link = CreateFileLinkOrIgnore(Path.Combine(_root, "parent-link.txt"), Path.Combine(_parent, "parent.txt"));

        Assert.That(PathContainment.IsWithin(_root, link), Is.False);
    }

    [Test]
    public void IsWithin_DanglingLinkPointingOutside_IsNotContained()
    {
        var link = CreateFileLinkOrIgnore(Path.Combine(_root, "dangling.txt"), Path.Combine(_sibling, "not-yet.txt"));

        Assert.That(PathContainment.IsWithin(_root, link), Is.False);
    }

    [Test]
    public void IsWithin_LinkPointingInsideRoot_IsContained()
    {
        Directory.CreateDirectory(Path.Combine(_root, "real"));
        var link = CreateDirectoryLinkOrIgnore(Path.Combine(_root, "alias"), Path.Combine(_root, "real"));

        Assert.That(PathContainment.IsWithin(_root, Path.Combine(link, "file.cs")), Is.True);
    }

    [Test]
    public void IsWithin_RootReachedThroughLink_ContainsRealAndLinkedPaths()
    {
        var linkedRoot = CreateDirectoryLinkOrIgnore(Path.Combine(_parent, "root-link"), _root);

        Assert.Multiple(() =>
        {
            Assert.That(PathContainment.IsWithin(linkedRoot, Path.Combine(_root, "inside.txt")), Is.True);
            Assert.That(PathContainment.IsWithin(_root, Path.Combine(linkedRoot, "inside.txt")), Is.True);
            Assert.That(PathContainment.IsWithin(linkedRoot, Path.Combine(linkedRoot, "missing", "new.txt")), Is.True);
            Assert.That(PathContainment.IsWithin(linkedRoot, Path.Combine(_sibling, "secret.txt")), Is.False);
        });
    }

    [Test]
    public async Task FileOperationsService_ReadThroughEscapingLink_IsRejected()
    {
        CreateDirectoryLinkOrIgnore(Path.Combine(_root, "escape"), _sibling);
        var service = new FileOperationsService(_root);

        var read = await service.ReadFileAsync(Path.Combine("escape", "secret.txt"));
        var write = await service.WriteFileAsync(Path.Combine("escape", "written.txt"), "x");

        Assert.Multiple(() =>
        {
            Assert.That(read.Success, Is.False);
            Assert.That(write.Success, Is.False);
            Assert.That(File.Exists(Path.Combine(_sibling, "written.txt")), Is.False);
        });
    }

    private static string CreateDirectoryLinkOrIgnore(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links cannot be created here: {ex.Message}");
        }

        return link;
    }

    private static string CreateFileLinkOrIgnore(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links cannot be created here: {ex.Message}");
        }

        return link;
    }

    #endregion

    #region Services using containment checks

    [Test]
    public async Task UniversalFileOperations_SiblingAndTraversalPaths_AreRejected()
    {
        var service = new UniversalFileOperations(_root, new FileOperationsService(_root));

        foreach (var path in new[] { Path.Combine(_sibling, "secret.txt"), "../root2/secret.txt", "../parent.txt" })
        {
            var content = await service.GetFileContentAsync(new FileContentRequest { Path = path });
            Assert.That(content.Error, Is.EqualTo("Path is outside project root"), path);
            Assert.That(content.Content, Is.Null, path);

            var info = await service.GetFileInfoAsync(new FileInfoRequest { Path = path });
            Assert.That(info.Error, Is.EqualTo("Path is outside project root"), path);

            var delete = await service.ExecuteFileOperationAsync(new FileOperationRequest { Path = path, Operation = FileOperationType.Delete });
            Assert.That(delete.Error, Is.EqualTo("Path is outside project root"), path);
        }

        Assert.That(File.Exists(Path.Combine(_sibling, "secret.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(_parent, "parent.txt")), Is.True);
    }

    [Test]
    public async Task UniversalFileOperations_CopyToSibling_IsRejected()
    {
        var service = new UniversalFileOperations(_root, new FileOperationsService(_root));

        var copy = await service.ExecuteFileOperationAsync(new FileOperationRequest
        {
            Path = "inside.txt",
            Operation = FileOperationType.Copy,
            DestinationPath = Path.Combine(_sibling, "copied.txt")
        });

        Assert.That(copy.Error, Is.EqualTo("Path is outside project root"));
        Assert.That(File.Exists(Path.Combine(_sibling, "copied.txt")), Is.False);
    }

    [Test]
    public async Task UniversalFileOperations_PathInsideRoot_IsRead()
    {
        var service = new UniversalFileOperations(_root, new FileOperationsService(_root));

        var content = await service.GetFileContentAsync(new FileContentRequest { Path = "inside.txt" });

        Assert.That(content.Error, Is.Null);
        Assert.That(content.Content, Is.EqualTo("inside"));
    }

    [Test]
    public async Task FileOperationsService_SiblingAndTraversalPaths_AreRejected()
    {
        var service = new FileOperationsService(_root);

        var sibling = await service.ReadFileAsync(Path.Combine(_sibling, "secret.txt"));
        var traversal = await service.ReadFileAsync("../root2/secret.txt");
        var write = await service.WriteFileAsync(Path.Combine(_sibling, "written.txt"), "x");

        Assert.That(sibling.Success, Is.False);
        Assert.That(sibling.Error, Does.Contain("outside project root"));
        Assert.That(traversal.Success, Is.False);
        Assert.That(traversal.Error, Does.Contain("outside project root"));
        Assert.That(write.Success, Is.False);
        Assert.That(File.Exists(Path.Combine(_sibling, "written.txt")), Is.False);
    }

    [Test]
    public void ProjectContextManager_SiblingAndTraversalPaths_AreInvalid()
    {
        var manager = new ProjectContextManager();
        manager.OpenProject(_root);

        Assert.That(manager.IsValidPath(Path.Combine(_root, "inside.txt")), Is.True);
        Assert.That(manager.IsValidPath(Path.Combine(_sibling, "secret.txt")), Is.False);
        Assert.That(manager.IsValidPath(Path.Combine(_root, "..", "parent.txt")), Is.False);
    }

    [Test]
    public void SecurityManager_DefaultPermissions_RejectSiblingAndTraversalPaths()
    {
        var security = new SecurityManager(NullLogger<SecurityManager>.Instance, _root);

        Assert.That(security.IsPathAllowed("analyzer", Path.Combine(_root, "inside.txt")), Is.True);
        Assert.That(security.IsPathAllowed("analyzer", Path.Combine(_sibling, "secret.txt")), Is.False);
        Assert.That(security.IsPathAllowed("analyzer", Path.Combine(_root, "..", "parent.txt")), Is.False);
    }

    #endregion
}
