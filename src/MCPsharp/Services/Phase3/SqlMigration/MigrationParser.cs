using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.SqlMigration;

namespace MCPsharp.Services.Phase3.SqlMigration;

/// <summary>
/// Responsible for parsing Entity Framework migration files and extracting operations
/// </summary>
internal class MigrationParser
{
    private readonly ILogger<MigrationParser> _logger;
    private readonly Dictionary<string, Regex> _sqlOperationPatterns;

    public MigrationParser(ILogger<MigrationParser> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _sqlOperationPatterns = new Dictionary<string, Regex>
        {
            ["CreateTable"] = new Regex(@"CreateTable\s*\(\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["AddColumn"] = new Regex(@"AddColumn\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["DropColumn"] = new Regex(@"DropColumn\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["AlterColumn"] = new Regex(@"AlterColumn\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["DropTable"] = new Regex(@"DropTable\s*\(\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["RenameTable"] = new Regex(@"RenameTable\s*\(\s*name:\s*[""']([^'""]+)[""']\s*,\s*newName:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["CreateIndex"] = new Regex(@"CreateIndex\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["DropIndex"] = new Regex(@"DropIndex\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["AddForeignKey"] = new Regex(@"AddForeignKey\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["DropForeignKey"] = new Regex(@"DropForeignKey\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["AddPrimaryKey"] = new Regex(@"AddPrimaryKey\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["DropPrimaryKey"] = new Regex(@"DropPrimaryKey\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["Sql"] = new Regex(@"Sql\s*\(\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        };
    }

    public async Task<MigrationInfo> ParseMigrationFileAsync(string migrationFilePath, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Parsing migration file: {FilePath}", migrationFilePath);

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
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse migration file: {FilePath}", migrationFilePath);
            throw;
        }

        return migration;
    }

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    public async Task<List<string>> GetMigrationFilesAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Searching for migration files in: {ProjectPath}", projectPath);

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

            foreach (var searchPath in searchPaths)
            {
                if (Directory.Exists(searchPath))
                {
                    var files = Directory.GetFiles(searchPath, "*.cs")
                        .Where(f => !Path.GetFileName(f).StartsWith("DesignTime"))
                        .OrderBy(f => f);

                    migrationFiles.AddRange(files);
                    _logger.LogDebug("Found {Count} migration files in {Path}", files.Count(), searchPath);
                }
            }

            // Also search the entire project if no specific directories found
            if (!migrationFiles.Any())
            {
                var allCsFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
                    .Where(f => Path.GetFileNameWithoutExtension(f).Length > 15) // EF migrations have long timestamps
                    .Where(f => !Path.GetFileName(f).StartsWith("DesignTime"))
                    .Where(f => !Path.GetFileName(f).Contains("ModelSnapshot"))
                    .OrderBy(f => f);

                migrationFiles.AddRange(allCsFiles);
                _logger.LogDebug("Found {Count} potential migration files in project", allCsFiles.Count());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search for migration files in: {ProjectPath}", projectPath);
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

        // Extract parameters
        if (invocation.ArgumentList != null)
        {
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (argument.Expression is LiteralExpressionSyntax _literal)
                {
                    // Handle literal arguments
                }
            }
        }

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

    private bool IsBreakingOperation(string operationType)
    {
        var breakingOperations = new[]
        {
            "DropTable", "DropColumn", "AlterColumn", "RenameTable",
            "DropIndex", "DropForeignKey", "DropPrimaryKey"
        };

        return breakingOperations.Contains(operationType, StringComparer.OrdinalIgnoreCase);
    }

    private List<string> ExtractMigrationDependencies(ClassDeclarationSyntax migrationClass)
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

    private string? ExtractStringArgument(AttributeSyntax attribute)
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

    private string CalculateChecksum(string content)
    {
        // Simple checksum implementation
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}
