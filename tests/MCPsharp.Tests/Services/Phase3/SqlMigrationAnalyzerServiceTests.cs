using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using FluentAssertions;
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

    public SqlMigrationAnalyzerServiceTests()
    {
        _mockLogger = Substitute.For<ILogger<SqlMigrationAnalyzerService>>();
        _service = new SqlMigrationAnalyzerService(_mockLogger);
    }

    [Fact]
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
        result.Should().Be(DatabaseProvider.SqlServer);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task GetMigrationFiles_ShouldReturnEmptyList_WhenNoMigrationsExist()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.GetMigrationFilesAsync(tempDir);

        // Assert
        result.Should().BeEmpty();

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task ParseMigrationFile_ShouldThrowFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var nonExistentFile = Path.Combine(Path.GetTempPath(), "nonexistent.cs");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.ParseMigrationFileAsync(nonExistentFile));
    }

    [Fact]
    public async Task GenerateMigrationReport_ShouldReturnValidReport_WhenProjectIsValid()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.GenerateMigrationReportAsync(tempDir, false);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata.ProjectPath.Should().Be(tempDir);
        result.Metadata.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        result.Metadata.TotalMigrations.Should().Be(0);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task ValidateMigrations_ShouldReturnEmptyList_WhenNoMigrationsExist()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.ValidateMigrationsAsync(tempDir);

        // Assert
        result.Should().BeEmpty();

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task EstimateMigrationExecution_ShouldReturnValidEstimate_ForValidInputs()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.EstimateMigrationExecutionAsync("TestMigration", tempDir);

        // Assert
        result.Should().NotBeNull();
        result.MigrationName.Should().Be("TestMigration");
        result.EstimatedExecutionTime.Should().BeGreaterOrEqualTo(TimeSpan.Zero);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task AnalyzeMigrations_ShouldReturnValidAnalysis_WhenProjectIsValid()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.AnalyzeMigrationsAsync(tempDir);

        // Assert
        result.Should().NotBeNull();
        result.Summary.Should().NotBeNull();
        result.Summary.TotalMigrations.Should().Be(0);
        result.Summary.AppliedMigrations.Should().Be(0);
        result.Summary.PendingMigrations.Should().Be(0);
        result.Migrations.Should().BeEmpty();
        result.BreakingChanges.Should().BeEmpty();

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task TrackSchemaEvolution_ShouldReturnEmptyEvolution_WhenNoMigrationsExist()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.TrackSchemaEvolutionAsync(tempDir);

        // Assert
        result.Should().NotBeNull();
        result.Snapshots.Should().BeEmpty();
        result.Changes.Should().BeEmpty();
        result.Metrics.Should().BeEmpty();

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task DetectBreakingChanges_ShouldReturnEmptyList_WhenMigrationsNotFound()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.DetectBreakingChangesAsync("NonExistent1", "NonExistent2", tempDir);

        // Assert
        result.Should().BeEmpty();

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task GetMigrationDependencies_ShouldReturnValidDependency_ForValidInputs()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();

        // Act
        var result = await _service.GetMigrationDependenciesAsync("TestMigration", tempDir);

        // Assert
        result.Should().NotBeNull();
        result.MigrationName.Should().Be("TestMigration");
        result.DependsOn.Should().BeEmpty();
        result.RequiredBy.Should().BeEmpty();

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task GetMigrationHistory_ShouldReturnEmptyList_WhenDbContextDoesNotExist()
    {
        // Arrange
        var nonExistentDbContext = Path.Combine(Path.GetTempPath(), "nonexistent.cs");

        // Act
        var result = await _service.GetMigrationHistoryAsync(nonExistentDbContext);

        // Assert
        result.Should().BeEmpty();
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

    [Fact]
    public async Task ParseMigrationFile_ShouldReturnValidMigrationInfo_ForValidMigrationFile()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();
        var migrationPath = CreateSampleMigrationFile(tempDir, "CreateTestTable");

        // Act
        var result = await _service.ParseMigrationFileAsync(migrationPath);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("CreateTestTable");
        result.FilePath.Should().Be(migrationPath);
        result.Operations.Should().NotBeEmpty();
        result.Operations.Should().Contain(o => o.Type.Equals("CreateTable", StringComparison.OrdinalIgnoreCase));

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task AnalyzeMigrations_ShouldDetectMigrations_WhenMigrationsExist()
    {
        // Arrange
        var tempDir = CreateTempProjectDirectory();
        CreateSampleMigrationFile(tempDir, "CreateTestTable");
        CreateSampleMigrationFile(tempDir, "AddColumnToTestTable");

        // Act
        var result = await _service.AnalyzeMigrationsAsync(tempDir);

        // Assert
        result.Should().NotBeNull();
        result.Summary.TotalMigrations.Should().Be(2);
        result.Migrations.Should().HaveCount(2);
        result.Migrations.Should().Contain(m => m.Name == "CreateTestTable");
        result.Migrations.Should().Contain(m => m.Name == "AddColumnToTestTable");

        // Cleanup
        Directory.Delete(tempDir, true);
    }
}