using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using MCPsharp.Models.SqlMigration;
using MCPsharp.Services.Phase3;

namespace MCPsharp.Tests.Services.Phase3;

/// <summary>
/// Tests for the SQL Migration Analyzer Service
/// Phase 3 implementation tests
/// </summary>
public class SqlMigrationAnalyzerServiceTests
{
    private readonly SqlMigrationAnalyzerService _service;
    private readonly ILogger<SqlMigrationAnalyzerService> _mockLogger;
    private readonly ILoggerFactory _mockLoggerFactory;

    public SqlMigrationAnalyzerServiceTests()
    {
        _mockLogger = Substitute.For<ILogger<SqlMigrationAnalyzerService>>();
        _mockLoggerFactory = Substitute.For<ILoggerFactory>();
        _service = new SqlMigrationAnalyzerService(_mockLogger, _mockLoggerFactory);
    }

    [Test]
    public async Task DetectDatabaseProvider_ShouldReturnSqlServer_WhenSqlServerPackageExists()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();
        var csprojPath = Path.Combine(tempDir, "TestProject.csproj");

        await File.WriteAllTextAsync(csprojPath, @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Microsoft.EntityFrameworkCore.SqlServer"" Version=""6.0.0"" />
  </ItemGroup>
</Project>");

        // Act
        var result = await _service.DetectDatabaseProviderAsync(tempDir);

        // Assert
        Assert.That(result, Is.EqualTo(DatabaseProvider.SqlServer));

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Test]
    public async Task GetMigrationFiles_ShouldReturnEmptyList_WhenNoMigrationsExist()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.GetMigrationFilesAsync(tempDir);

        // Assert
        Assert.That(result, Is.Empty);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Test]
    public async Task ParseMigrationFile_ShouldThrowFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var nonExistentFile = Path.Combine(Path.GetTempPath(), "nonexistent.cs");

        // Act & Assert
        Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await _service.ParseMigrationFileAsync(nonExistentFile));
    }

    [Test]
    public async Task GenerateMigrationReport_ShouldReturnValidReport_WhenProjectIsValid()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.GenerateMigrationReportAsync(tempDir, false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Metadata, Is.Not.Null);
        Assert.That(result.Metadata.ProjectPath, Is.EqualTo(tempDir));
        Assert.That(result.Metadata.GeneratedAt, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(1)));
        Assert.That(result.Metadata.TotalMigrations, Is.EqualTo(0));

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Test]
    public async Task ValidateMigrations_ShouldReturnEmptyList_WhenNoMigrationsExist()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.ValidateMigrationsAsync(tempDir);

        // Assert
        Assert.That(result, Is.Empty);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Test]
    public async Task EstimateMigrationExecution_ShouldReturnValidEstimate_ForValidInputs()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.EstimateMigrationExecutionAsync("TestMigration", tempDir);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MigrationName, Is.EqualTo("TestMigration"));
        Assert.That(result.EstimatedExecutionTime, Is.GreaterThanOrEqualTo(TimeSpan.Zero));

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Test]
    public async Task AnalyzeMigrations_ShouldReturnValidAnalysis_WhenProjectIsValid()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.AnalyzeMigrationsAsync(tempDir);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Summary, Is.Not.Null);
        Assert.That(result.Summary.TotalMigrations, Is.EqualTo(0));
        Assert.That(result.Summary.AppliedMigrations, Is.EqualTo(0));
        Assert.That(result.Summary.PendingMigrations, Is.EqualTo(0));
        Assert.That(result.Migrations, Is.Empty);
        Assert.That(result.BreakingChanges, Is.Empty);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Test]
    public async Task TrackSchemaEvolution_ShouldReturnEmptyEvolution_WhenNoMigrationsExist()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.TrackSchemaEvolutionAsync(tempDir);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Snapshots, Is.Empty);
        Assert.That(result.Changes, Is.Empty);
        Assert.That(result.Metrics, Is.Empty);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Test]
    public async Task DetectBreakingChanges_ShouldReturnEmptyList_WhenMigrationsNotFound()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.DetectBreakingChangesAsync("NonExistent1", "NonExistent2", tempDir);

        // Assert
        Assert.That(result, Is.Empty);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Test]
    public async Task GetMigrationDependencies_ShouldReturnValidDependency_ForValidInputs()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.GetMigrationDependenciesAsync("TestMigration", tempDir);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MigrationName, Is.EqualTo("TestMigration"));
        Assert.That(result.DependsOn, Is.Empty);
        Assert.That(result.RequiredBy, Is.Empty);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Test]
    public async Task GetMigrationHistory_ShouldReturnEmptyList_WhenDbContextDoesNotExist()
    {
        // Arrange
        var nonExistentDbContext = Path.Combine(Path.GetTempPath(), "nonexistent.cs");

        // Act
        var result = await _service.GetMigrationHistoryAsync(nonExistentDbContext);

        // Assert
        Assert.That(result, Is.Empty);
    }

    private string CreateTempProjectDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        // Create a minimal .csproj file
        var csprojContent = @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
  </PropertyGroup>
</Project>";

        File.WriteAllText(Path.Combine(tempDir, "TestProject.csproj"), csprojContent);

        return tempDir;
    }

    private string CreateSampleMigrationFile(string tempDir, string migrationName)
    {
        var migrationsDir = Path.Combine(tempDir, "Migrations");
        Directory.CreateDirectory(migrationsDir);

        var migrationContent = $@"
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestProject.Migrations
{{
    public partial class {migrationName} : Migration
    {{
        protected override void Up(MigrationBuilder migrationBuilder)
        {{
            migrationBuilder.CreateTable(
                name: ""TestTable"",
                columns: table => new
                {{
                    Id = table.Column<int>(type: ""int"", nullable: false)
                        .Annotation(""SqlServer:Identity"", ""1, 1""),
                    Name = table.Column<string>(type: ""nvarchar(max)"", nullable: true)
                }},
                constraints: table =>
                {{
                    table.PrimaryKey(""PK_TestTable"", x => x.Id);
                }});
        }}

        protected override void Down(MigrationBuilder migrationBuilder)
        {{
            migrationBuilder.DropTable(
                name: ""TestTable"");
        }}
    }}
}}";

        var migrationPath = Path.Combine(migrationsDir, $"{migrationName}.cs");
        File.WriteAllText(migrationPath, migrationContent);

        return migrationPath;
    }

    [Test]
    public async Task ParseMigrationFile_ShouldReturnValidMigrationInfo_ForValidMigrationFile()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();
        var migrationPath = CreateSampleMigrationFile(tempDir, "CreateTestTable");

        // Act
        var result = await _service.ParseMigrationFileAsync(migrationPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("CreateTestTable"));
        Assert.That(result.FilePath, Is.EqualTo(migrationPath));
        Assert.That(result.Operations, Is.Not.Empty);
        Assert.That(result.Operations, Has.Some.Matches<dynamic>(o => o.Type.Equals("CreateTable", StringComparison.OrdinalIgnoreCase)));

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Test]
    public async Task AnalyzeMigrations_ShouldDetectMigrations_WhenMigrationsExist()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();
        CreateSampleMigrationFile(tempDir, "CreateTestTable");
        CreateSampleMigrationFile(tempDir, "AddColumnToTestTable");

        // Act
        var result = await _service.AnalyzeMigrationsAsync(tempDir);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Summary.TotalMigrations, Is.EqualTo(2));
        Assert.That(result.Migrations, Has.Count.EqualTo(2));
        Assert.That(result.Migrations, Has.Some.Matches<dynamic>(m => m.Name == "CreateTestTable"));
        Assert.That(result.Migrations, Has.Some.Matches<dynamic>(m => m.Name == "AddColumnToTestTable"));

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _mockLoggerFactory?.Dispose();
    }
}