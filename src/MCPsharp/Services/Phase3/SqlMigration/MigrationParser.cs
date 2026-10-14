using System.Text.RegularExpressions;
using MCPsharp.Models.SqlMigration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Phase3.SqlMigration;

/// <summary>
/// Responsible for parsing Entity Framework migration files and extracting operations
/// </summary>
internal sealed partial class MigrationParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly ILogger<MigrationParser> _logger;
    private readonly Dictionary<string, Regex> _sqlOperationPatterns;

    public MigrationParser(ILogger<MigrationParser> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _sqlOperationPatterns = new Dictionary<string, Regex>
        {
            ["CreateTable"] = new Regex(@"CreateTable\s*\(\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout),
            ["AddColumn"] = new Regex(@"AddColumn\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout),
            ["DropColumn"] = new Regex(@"DropColumn\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout),
            ["AlterColumn"] = new Regex(@"AlterColumn\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout),
            ["DropTable"] = new Regex(@"DropTable\s*\(\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout),
            ["RenameTable"] = new Regex(@"RenameTable\s*\(\s*name:\s*[""']([^'""]+)[""']\s*,\s*newName:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout),
            ["CreateIndex"] = new Regex(@"CreateIndex\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout),
            ["DropIndex"] = new Regex(@"DropIndex\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout),
            ["AddForeignKey"] = new Regex(@"AddForeignKey\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout),
            ["DropForeignKey"] = new Regex(@"DropForeignKey\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout),
            ["AddPrimaryKey"] = new Regex(@"AddPrimaryKey\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout),
            ["DropPrimaryKey"] = new Regex(@"DropPrimaryKey\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout),
            ["Sql"] = new Regex(@"Sql\s*\(\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout)
        };
    }

    public async Task<MigrationInfo> ParseMigrationFileAsync(string migrationFilePath, CancellationToken cancellationToken = default)
    {
        LogParsingMigrationFile(_logger, migrationFilePath);

        if (!File.Exists(migrationFilePath))
            throw new FileNotFoundException($"Migration file not found: {migrationFilePath}");

        var migration = new MigrationInfo
        {
            FilePath = migrationFilePath,
            Name = Path.GetFileNameWithoutExtension(migrationFilePath),
            CreatedAt = File.GetCreationTime(migrationFilePath)
        };

        try
        {
            var code = await File.ReadAllTextAsync(migrationFilePath, cancellationToken);
            var syntaxTree = CSharpSyntaxTree.ParseText(code, cancellationToken: cancellationToken);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            // Find the migration class
            var migrationClass = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.BaseList?.Types.Any(t => t.ToString().Contains("Migration")) == true);

            if (migrationClass != null)
            {
                // Extract DbContext name
                var dbContextAttr = migrationClass.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .FirstOrDefault(a => a.Name.ToString().Contains("DbContext"));

                if (dbContextAttr != null)
                {
                    migration.DbContextName = ExtractStringArgument(dbContextAttr);
                }

                // Parse Up and Down methods
                var upMethod = migrationClass.Members
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == "Up");

                var downMethod = migrationClass.Members
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == "Down");

                if (upMethod != null)
                {
                    migration.Operations.AddRange(ParseMigrationOperations(upMethod, OperationDirection.Up));
                }

                if (downMethod != null)
                {
                    migration.Operations.AddRange(ParseMigrationOperations(downMethod, OperationDirection.Down));
                }

                // Extract dependencies from attributes or comments
                migration.Dependencies = ExtractMigrationDependencies(migrationClass);
            }

            // Calculate checksum
            migration.Checksum = CalculateChecksum(code);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogMigrationFileParseFailed(_logger, ex, migrationFilePath);
            throw new InvalidOperationException($"Failed to parse migration file '{migrationFilePath}': {ex.Message}", ex);
        }

        return migration;
    }

    public Task<List<string>> GetMigrationFilesAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(GetMigrationFiles(projectPath));
        }
        catch (Exception ex)
        {
            return Task.FromException<List<string>>(ex);
        }
    }

    private List<string> GetMigrationFiles(string projectPath)
    {
        LogSearchingMigrationFiles(_logger, projectPath);

        var migrationFiles = new List<string>();

        try
        {
            // Common migration directory patterns
            var searchPaths = new[]
            {
                Path.Combine(projectPath, "Migrations"),
                Path.Combine(projectPath, "Data", "Migrations"),
                Path.Combine(projectPath, "Infrastructure", "Migrations"),
                Path.Combine(projectPath, "Persistence", "Migrations")
            };

            foreach (var searchPath in searchPaths.Where(Directory.Exists))
            {
                var files = Directory.GetFiles(searchPath, "*.cs")
                    .Where(f => !Path.GetFileName(f).StartsWith("DesignTime", StringComparison.Ordinal))
                    .OrderBy(f => f)
                    .ToList();

                migrationFiles.AddRange(files);
                LogFoundMigrationFilesInPath(_logger, files.Count, searchPath);
            }

            // Also search the entire project if no specific directories found
            if (migrationFiles.Count == 0)
            {
                var allCsFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
                    .Where(f => Path.GetFileNameWithoutExtension(f).Length > 15) // EF migrations have long timestamps
                    .Where(f => !Path.GetFileName(f).StartsWith("DesignTime", StringComparison.Ordinal))
                    .Where(f => !Path.GetFileName(f).Contains("ModelSnapshot"))
                    .OrderBy(f => f)
                    .ToList();

                migrationFiles.AddRange(allCsFiles);
                LogFoundPotentialMigrationFiles(_logger, allCsFiles.Count);
            }
        }
        catch (Exception ex)
        {
            LogMigrationFileSearchFailed(_logger, ex, projectPath);
        }

        return migrationFiles.Distinct().ToList();
    }

    private List<MigrationOperation> ParseMigrationOperations(MethodDeclarationSyntax method, OperationDirection direction)
    {
        var operations = new List<MigrationOperation>();

        // Parse migrationBuilder calls in the method body
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var operation = ParseInvocationExpression(invocation, direction);
            if (operation != null)
            {
                operations.Add(operation);
            }
        }

        return operations;
    }

    private MigrationOperation? ParseInvocationExpression(InvocationExpressionSyntax invocation, OperationDirection direction)
    {
        // Parse migrationBuilder.CreateTable(), AddColumn(), etc.
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess?.Name?.Identifier.Text == null)
            return null;

        var operationType = memberAccess.Name.Identifier.Text;
        var operation = new MigrationOperation
        {
            Type = operationType,
            Direction = direction,
            IsBreakingChange = IsBreakingOperation(operationType)
        };

        // Use regex patterns to extract table/column names
        var fullText = invocation.ToFullString();

        foreach (var pattern in _sqlOperationPatterns)
        {
            var match = pattern.Value.Match(fullText);
            if (match.Success)
            {
                switch (pattern.Key)
                {
                    case "CreateTable":
                    case "DropTable":
                    case "RenameTable":
                        operation.TableName = match.Groups[1].Value;
                        break;
                    case "AddColumn":
                    case "DropColumn":
                    case "AlterColumn":
                        operation.TableName = match.Groups[1].Value;
                        operation.ColumnName = match.Groups[2].Value;
                        operation.AffectedColumns.Add(match.Groups[2].Value);
                        break;
                    case "CreateIndex":
                    case "DropIndex":
                    case "AddForeignKey":
                    case "DropForeignKey":
                    case "AddPrimaryKey":
                    case "DropPrimaryKey":
                        operation.TableName = match.Groups[1].Value;
                        break;
                    case "Sql":
                        operation.SqlStatement = match.Groups[1].Value;
                        break;
                }
                break;
            }
        }

        return operation;
    }

    private static bool IsBreakingOperation(string operationType)
    {
        var breakingOperations = new[]
        {
            "DropTable", "DropColumn", "AlterColumn", "RenameTable",
            "DropIndex", "DropForeignKey", "DropPrimaryKey"
        };

        return breakingOperations.Contains(operationType, StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> ExtractMigrationDependencies(ClassDeclarationSyntax migrationClass)
    {
        var dependencies = new List<string>();

        // Extract dependencies from attributes
        var attributes = migrationClass.AttributeLists
            .SelectMany(al => al.Attributes)
            .Where(a => a.Name.ToString().Contains("Migration"));

        foreach (var attr in attributes)
        {
            // Parse dependency information from attributes
            // This is a simplified implementation
        }

        return dependencies;
    }

    private static string? ExtractStringArgument(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList?.Arguments.Any() == true)
        {
            var firstArg = attribute.ArgumentList.Arguments[0];
            if (firstArg.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }
        }
        return null;
    }

    private static string CalculateChecksum(string content)
    {
        // Simple checksum implementation
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(hash);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Parsing migration file: {FilePath}")]
    private static partial void LogParsingMigrationFile(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to parse migration file: {FilePath}")]
    private static partial void LogMigrationFileParseFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Searching for migration files in: {ProjectPath}")]
    private static partial void LogSearchingMigrationFiles(ILogger logger, string projectPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} migration files in {Path}")]
    private static partial void LogFoundMigrationFilesInPath(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} potential migration files in project")]
    private static partial void LogFoundPotentialMigrationFiles(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to search for migration files in: {ProjectPath}")]
    private static partial void LogMigrationFileSearchFailed(ILogger logger, Exception exception, string projectPath);
}
