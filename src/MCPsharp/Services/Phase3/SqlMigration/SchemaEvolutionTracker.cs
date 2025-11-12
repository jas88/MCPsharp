using Microsoft.Extensions.Logging;
using MCPsharp.Models.SqlMigration;

namespace MCPsharp.Services.Phase3.SqlMigration;

/// <summary>
/// Tracks schema evolution across migrations
/// </summary>
internal class SchemaEvolutionTracker
{
    private readonly ILogger<SchemaEvolutionTracker> _logger;

    public SchemaEvolutionTracker(ILogger<SchemaEvolutionTracker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SchemaEvolution TrackSchemaEvolution(List<MigrationInfo> migrations)
    {
        _logger.LogInformation("Tracking schema evolution for {MigrationCount} migrations", migrations.Count);

        var evolution = new SchemaEvolution();
        var snapshots = new List<SchemaSnapshot>();
        var changes = new List<SchemaChange>();

        // Build schema snapshots for each migration
        var currentSchema = new Dictionary<string, TableDefinition>();

        foreach (var migration in migrations.OrderBy(m => m.CreatedAt))
        {
            var snapshot = new SchemaSnapshot
            {
                MigrationName = migration.Name,
                Timestamp = migration.CreatedAt,
                Tables = new List<TableDefinition>(currentSchema.Values.ToList())
            };

            // Apply migration operations to create new schema
            var updatedSchema = ApplyMigrationOperations(currentSchema, migration);

            // Calculate changes
            var migrationChanges = CalculateSchemaChanges(currentSchema, updatedSchema, migration);
            changes.AddRange(migrationChanges);

            // Update current schema
            currentSchema = updatedSchema;
            snapshot.Tables = new List<TableDefinition>(currentSchema.Values.ToList());

            snapshots.Add(snapshot);
        }

        evolution.Snapshots = snapshots;
        evolution.Changes = changes;
        evolution.Metrics = CalculateEvolutionMetrics(snapshots, changes);

        return evolution;
    }

    public void BuildSchemaChanges(MigrationAnalysisResult result)
    {
        var schemaChanges = new Dictionary<string, List<SchemaChange>>();

        foreach (var migration in result.Migrations)
        {
            foreach (var operation in migration.Operations)
            {
                var change = new SchemaChange
                {
                    ChangeType = GetChangeType(operation.Type),
                    ObjectType = GetObjectType(operation.Type),
                    ObjectName = operation.TableName ?? operation.ColumnName ?? "Unknown",
                    SchemaName = operation.SchemaName,
                    MigrationName = migration.Name,
                    ChangedAt = migration.CreatedAt
                };

                if (!schemaChanges.ContainsKey(migration.Name))
                    schemaChanges[migration.Name] = new List<SchemaChange>();

                schemaChanges[migration.Name].Add(change);
            }
        }

        result.SchemaChanges = schemaChanges;
    }

    private Dictionary<string, TableDefinition> ApplyMigrationOperations(Dictionary<string, TableDefinition> currentSchema, MigrationInfo migration)
    {
        var newSchema = new Dictionary<string, TableDefinition>(currentSchema);

        foreach (var kvp in currentSchema)
        {
            newSchema[kvp.Key] = CloneTableDefinition(kvp.Value);
        }

        // Apply migration operations to create new schema
        foreach (var operation in migration.Operations.Where(o => o.Direction == OperationDirection.Up))
        {
            switch (operation.Type.ToLowerInvariant())
            {
                case "createtable":
                    if (!string.IsNullOrEmpty(operation.TableName))
                    {
                        newSchema[operation.TableName] = new TableDefinition
                        {
                            Name = operation.TableName,
                            Schema = operation.SchemaName
                        };
                    }
                    break;
                case "droptable":
                    if (!string.IsNullOrEmpty(operation.TableName))
                    {
                        newSchema.Remove(operation.TableName);
                    }
                    break;
                // Handle other operation types...
            }
        }

        return newSchema;
    }

    private TableDefinition CloneTableDefinition(TableDefinition table)
    {
        return new TableDefinition
        {
            Name = table.Name,
            Schema = table.Schema,
            Columns = table.Columns.Select(c => new ColumnDefinition
            {
                Name = c.Name,
                DataType = c.DataType,
                IsNullable = c.IsNullable,
                IsPrimaryKey = c.IsPrimaryKey,
                IsForeignKey = c.IsForeignKey,
                DefaultValue = c.DefaultValue,
                MaxLength = c.MaxLength,
                Precision = c.Precision,
                Scale = c.Scale
            }).ToList(),
            PrimaryKey = table.PrimaryKey,
            ForeignKeys = new List<string>(table.ForeignKeys),
            Indexes = new List<string>(table.Indexes)
        };
    }

    private List<SchemaChange> CalculateSchemaChanges(Dictionary<string, TableDefinition> oldSchema, Dictionary<string, TableDefinition> newSchema, MigrationInfo migration)
    {
        var changes = new List<SchemaChange>();

        // Compare schemas to find changes
        var allTables = oldSchema.Keys.Union(newSchema.Keys).ToList();

        foreach (var tableName in allTables)
        {
            var oldTable = oldSchema.GetValueOrDefault(tableName);
            var newTable = newSchema.GetValueOrDefault(tableName);

            if (oldTable == null && newTable != null)
            {
                // Table was created
                changes.Add(new SchemaChange
                {
                    ChangeType = "Create",
                    ObjectType = "Table",
                    ObjectName = tableName,
                    NewDefinition = new Dictionary<string, object>(),
                    MigrationName = migration.Name,
                    ChangedAt = migration.CreatedAt
                });
            }
            else if (oldTable != null && newTable == null)
            {
                // Table was dropped
                changes.Add(new SchemaChange
                {
                    ChangeType = "Drop",
                    ObjectType = "Table",
                    ObjectName = tableName,
                    OldDefinition = new Dictionary<string, object>(),
                    MigrationName = migration.Name,
                    ChangedAt = migration.CreatedAt
                });
            }
            else if (oldTable != null && newTable != null)
            {
                // Table was altered
                var tableChanges = CalculateTableChanges(oldTable, newTable, migration);
                changes.AddRange(tableChanges);
            }
        }

        return changes;
    }

    private List<SchemaChange> CalculateTableChanges(TableDefinition oldTable, TableDefinition newTable, MigrationInfo migration)
    {
        var changes = new List<SchemaChange>();

        // Compare columns
        var oldColumns = oldTable.Columns.ToDictionary(c => c.Name);
        var newColumns = newTable.Columns.ToDictionary(c => c.Name);

        var allColumns = oldColumns.Keys.Union(newColumns.Keys).ToList();

        foreach (var columnName in allColumns)
        {
            var oldColumn = oldColumns.GetValueOrDefault(columnName);
            var newColumn = newColumns.GetValueOrDefault(columnName);

            if (oldColumn == null && newColumn != null)
            {
                // Column was added
                changes.Add(new SchemaChange
                {
                    ChangeType = "Create",
                    ObjectType = "Column",
                    ObjectName = columnName,
                    NewDefinition = new Dictionary<string, object>(),
                    MigrationName = migration.Name,
                    ChangedAt = migration.CreatedAt
                });
            }
            else if (oldColumn != null && newColumn == null)
            {
                // Column was dropped
                changes.Add(new SchemaChange
                {
                    ChangeType = "Drop",
                    ObjectType = "Column",
                    ObjectName = columnName,
                    OldDefinition = new Dictionary<string, object>(),
                    MigrationName = migration.Name,
                    ChangedAt = migration.CreatedAt
                });
            }
            else if (oldColumn != null && newColumn != null)
            {
                // Column was altered
                if (!AreColumnsEqual(oldColumn, newColumn))
                {
                    changes.Add(new SchemaChange
                    {
                        ChangeType = "Alter",
                        ObjectType = "Column",
                        ObjectName = columnName,
                        OldDefinition = new Dictionary<string, object>(),
                        NewDefinition = new Dictionary<string, object>(),
                        MigrationName = migration.Name,
                        ChangedAt = migration.CreatedAt
                    });
                }
            }
        }

        return changes;
    }

    private bool AreColumnsEqual(ColumnDefinition col1, ColumnDefinition col2)
    {
        return col1.DataType == col2.DataType &&
               col1.IsNullable == col2.IsNullable &&
               col1.MaxLength == col2.MaxLength &&
               col1.Precision == col2.Precision &&
               col1.Scale == col2.Scale &&
               col1.DefaultValue == col2.DefaultValue;
    }

    private Dictionary<string, EvolutionMetric> CalculateEvolutionMetrics(List<SchemaSnapshot> snapshots, List<SchemaChange> changes)
    {
        var metrics = new Dictionary<string, EvolutionMetric>();

        if (snapshots.Count < 2)
            return metrics;

        var initialSnapshot = snapshots.First();
        var finalSnapshot = snapshots.Last();

        // Table count evolution
        metrics["TableCount"] = new EvolutionMetric
        {
            MetricName = "Table Count",
            InitialValue = initialSnapshot.Tables.Count,
            FinalValue = finalSnapshot.Tables.Count,
            ChangePercentage = finalSnapshot.Tables.Count > 0 ?
                ((double)(finalSnapshot.Tables.Count - initialSnapshot.Tables.Count) / initialSnapshot.Tables.Count) * 100 : 0,
            Trend = finalSnapshot.Tables.Count > initialSnapshot.Tables.Count ? "Increasing" :
                    finalSnapshot.Tables.Count < initialSnapshot.Tables.Count ? "Decreasing" : "Stable"
        };

        // Column count evolution
        var initialColumnCount = initialSnapshot.Tables.Sum(t => t.Columns.Count);
        var finalColumnCount = finalSnapshot.Tables.Sum(t => t.Columns.Count);

        metrics["ColumnCount"] = new EvolutionMetric
        {
            MetricName = "Column Count",
            InitialValue = initialColumnCount,
            FinalValue = finalColumnCount,
            ChangePercentage = initialColumnCount > 0 ?
                ((double)(finalColumnCount - initialColumnCount) / initialColumnCount) * 100 : 0,
            Trend = finalColumnCount > initialColumnCount ? "Increasing" :
                    finalColumnCount < initialColumnCount ? "Decreasing" : "Stable"
        };

        return metrics;
    }

    private string GetChangeType(string operationType)
    {
        return operationType.ToLowerInvariant() switch
        {
            "create" or "add" => "Create",
            "drop" or "remove" => "Drop",
            "alter" or "modify" or "rename" => "Alter",
            _ => "Unknown"
        };
    }

    private string GetObjectType(string operationType)
    {
        return operationType.ToLowerInvariant() switch
        {
            var t when t.Contains("table") => "Table",
            var t when t.Contains("column") => "Column",
            var t when t.Contains("index") => "Index",
            var t when t.Contains("key") => "Constraint",
            var t when t.Contains("foreign") => "ForeignKey",
            _ => "Unknown"
        };
    }
}
