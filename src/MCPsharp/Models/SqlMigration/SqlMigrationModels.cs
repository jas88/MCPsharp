using System.ComponentModel.DataAnnotations;

namespace MCPsharp.Models.SqlMigration;

/// <summary>
/// Represents information about a database migration
/// </summary>
public class MigrationInfo
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? DbContextName { get; set; }
    public string? DbContextPath { get; set; }
    public List<MigrationOperation> Operations { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public DatabaseProvider Provider { get; set; }
    public bool IsApplied { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? Checksum { get; set; }
}

/// <summary>
/// Represents a single migration operation
/// </summary>
public class MigrationOperation
{
    public string Type { get; set; } = string.Empty; // CreateTable, AddColumn, DropTable, etc.
    public string? TableName { get; set; }
    public string? SchemaName { get; set; }
    public string? ColumnName { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string? SqlStatement { get; set; }
    public OperationDirection Direction { get; set; }
    public bool IsBreakingChange { get; set; }
    public List<string> AffectedColumns { get; set; } = new();
    public List<string> RiskAssessment { get; set; } = new();
}

/// <summary>
/// Migration analysis results
/// </summary>
public class MigrationAnalysisResult
{
    public List<MigrationInfo> Migrations { get; set; } = new();
    public List<BreakingChange> BreakingChanges { get; set; } = new();
    public List<MigrationDependency> Dependencies { get; set; } = new();
    public Dictionary<string, List<SchemaChange>> SchemaChanges { get; set; } = new();
    public AnalysisSummary Summary { get; set; } = new();
}

/// <summary>
/// Represents a breaking change between migrations
/// </summary>
public class BreakingChange
{
    public string Type { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? FromMigration { get; set; }
    public string? ToMigration { get; set; }
    public string? TableName { get; set; }
    public string? ColumnName { get; set; }
    public string? Recommendation { get; set; }
    public List<string> AffectedCodeLocations { get; set; } = new();
}

/// <summary>
/// Migration dependency information
/// </summary>
public class MigrationDependency
{
    public string MigrationName { get; set; } = string.Empty;
    public List<string> DependsOn { get; set; } = new();
    public List<string> RequiredBy { get; set; } = new();
    public DependencyType Type { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Schema change tracking
/// </summary>
public class SchemaChange
{
    public string ChangeType { get; set; } = string.Empty; // Create, Alter, Drop
    public string ObjectType { get; set; } = string.Empty; // Table, Column, Index, Constraint
    public string ObjectName { get; set; } = string.Empty;
    public string? SchemaName { get; set; }
    public Dictionary<string, object> OldDefinition { get; set; } = new();
    public Dictionary<string, object> NewDefinition { get; set; } = new();
    public string? MigrationName { get; set; }
    public DateTime ChangedAt { get; set; }
}

/// <summary>
/// Migration history from database
/// </summary>
public class MigrationHistory
{
    public string MigrationName { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; }
    public string? Checksum { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Comprehensive migration report
/// </summary>
public class MigrationReport
{
    public ReportMetadata Metadata { get; set; } = new();
    public List<MigrationInfo> Migrations { get; set; } = new();
    public List<BreakingChange> BreakingChanges { get; set; } = new();
    public SchemaEvolution Evolution { get; set; } = new();
    public RiskAssessment RiskAssessment { get; set; } = new();
    public Recommendations Recommendations { get; set; } = new();
    public Dictionary<string, object> Statistics { get; set; } = new();
}

/// <summary>
/// Report metadata
/// </summary>
public class ReportMetadata
{
    public string ProjectPath { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public string AnalyzerVersion { get; set; } = string.Empty;
    public DatabaseProvider Provider { get; set; }
    public int TotalMigrations { get; set; }
    public TimeSpan AnalysisTime { get; set; }
}

/// <summary>
/// Schema evolution tracking
/// </summary>
public class SchemaEvolution
{
    public List<SchemaSnapshot> Snapshots { get; set; } = new();
    public List<SchemaChange> Changes { get; set; } = new();
    public Dictionary<string, EvolutionMetric> Metrics { get; set; } = new();
}

/// <summary>
/// Schema snapshot at a point in time
/// </summary>
public class SchemaSnapshot
{
    public string MigrationName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public List<TableDefinition> Tables { get; set; } = new();
    public List<IndexDefinition> Indexes { get; set; } = new();
    public List<ConstraintDefinition> Constraints { get; set; } = new();
}

/// <summary>
/// Risk assessment for migrations
/// </summary>
public class RiskAssessment
{
    public RiskLevel OverallRisk { get; set; }
    public List<MigrationRisk> MigrationRisks { get; set; } = new();
    public List<string> HighRiskOperations { get; set; } = new();
    public List<string> DataLossRisks { get; set; } = new();
}

/// <summary>
/// Migration recommendations
/// </summary>
public class Recommendations
{
    public List<string> BestPractices { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Optimizations { get; set; } = new();
    public List<string> SecurityConsiderations { get; set; } = new();
}

/// <summary>
/// Analysis summary
/// </summary>
public class AnalysisSummary
{
    public int TotalMigrations { get; set; }
    public int AppliedMigrations { get; set; }
    public int PendingMigrations { get; set; }
    public int BreakingChanges { get; set; }
    public int HighRiskOperations { get; set; }
    public DatabaseProvider Provider { get; set; }
    public TimeSpan TotalAnalysisTime { get; set; }
}

/// <summary>
/// Table definition
/// </summary>
public class TableDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Schema { get; set; }
    public List<ColumnDefinition> Columns { get; set; } = new();
    public string? PrimaryKey { get; set; }
    public List<string> ForeignKeys { get; set; } = new();
    public List<string> Indexes { get; set; } = new();
}

/// <summary>
/// Column definition
/// </summary>
public class ColumnDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
    public string? DefaultValue { get; set; }
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
}

/// <summary>
/// Index definition
/// </summary>
public class IndexDefinition
{
    public string Name { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public bool IsUnique { get; set; }
    public bool IsClustered { get; set; }
    public string? FilterExpression { get; set; }
}

/// <summary>
/// Constraint definition
/// </summary>
public class ConstraintDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // PK, FK, UNIQUE, CHECK
    public string TableName { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public string? ReferencedTable { get; set; }
    public List<string>? ReferencedColumns { get; set; }
    public string? CheckExpression { get; set; }
}

/// <summary>
/// Migration risk
/// </summary>
public class MigrationRisk
{
    public string MigrationName { get; set; } = string.Empty;
    public RiskLevel RiskLevel { get; set; }
    public List<string> RiskFactors { get; set; } = new();
    public List<string> MitigationStrategies { get; set; } = new();
}

/// <summary>
/// Evolution metrics
/// </summary>
public class EvolutionMetric
{
    public string MetricName { get; set; } = string.Empty;
    public object InitialValue { get; set; } = new();
    public object FinalValue { get; set; } = new();
    public double ChangePercentage { get; set; }
    public string Trend { get; set; } = string.Empty;
}

// Enums
public enum DatabaseProvider
{
    SqlServer,
    PostgreSQL,
    MySQL,
    SQLite,
    Unknown
}

public enum OperationDirection
{
    Up,
    Down
}

public enum Severity
{
    Low,
    Medium,
    High,
    Critical
}

public enum DependencyType
{
    Direct,
    Indirect,
    Implicit,
    Explicit
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}