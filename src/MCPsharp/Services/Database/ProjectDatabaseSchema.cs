namespace MCPsharp.Services.Database;

/// <summary>
/// Contains SQL schema definitions for the project cache database.
/// </summary>
public static class ProjectDatabaseSchema
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// SQL to create the schema version tracking table.
    /// </summary>
    public const string CreateVersionTable = @"
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER NOT NULL,
    applied_at TEXT NOT NULL
);";

    /// <summary>
    /// SQL to create the projects table.
    /// </summary>
    public const string CreateProjectsTable = @"
CREATE TABLE IF NOT EXISTS projects (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    root_path_hash TEXT NOT NULL UNIQUE,
    root_path TEXT NOT NULL,
    name TEXT NOT NULL,
    opened_at TEXT NOT NULL,
    solution_count INTEGER NOT NULL DEFAULT 0,
    project_count INTEGER NOT NULL DEFAULT 0,
    file_count INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_projects_hash ON projects(root_path_hash);";

    /// <summary>
    /// SQL to create the files table.
    /// </summary>
    public const string CreateFilesTable = @"
CREATE TABLE IF NOT EXISTS files (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL,
    relative_path TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    last_indexed TEXT NOT NULL,
    size_bytes INTEGER NOT NULL DEFAULT 0,
    language TEXT NOT NULL,
    FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
    UNIQUE(project_id, relative_path)
);

CREATE INDEX IF NOT EXISTS idx_files_project ON files(project_id);
CREATE INDEX IF NOT EXISTS idx_files_path ON files(relative_path);
CREATE INDEX IF NOT EXISTS idx_files_hash ON files(content_hash);";

    /// <summary>
    /// SQL to create the symbols table.
    /// </summary>
    public const string CreateSymbolsTable = @"
CREATE TABLE IF NOT EXISTS symbols (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    file_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    kind TEXT NOT NULL,
    namespace TEXT,
    containing_type TEXT,
    line INTEGER NOT NULL,
    column INTEGER NOT NULL,
    end_line INTEGER NOT NULL,
    end_column INTEGER NOT NULL,
    accessibility TEXT NOT NULL,
    signature TEXT,
    FOREIGN KEY (file_id) REFERENCES files(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_symbols_file ON symbols(file_id);
CREATE INDEX IF NOT EXISTS idx_symbols_name ON symbols(name);
CREATE INDEX IF NOT EXISTS idx_symbols_kind ON symbols(kind);
CREATE INDEX IF NOT EXISTS idx_symbols_namespace ON symbols(namespace);
CREATE INDEX IF NOT EXISTS idx_symbols_name_kind ON symbols(name, kind);";

    /// <summary>
    /// SQL to create the references table for cross-reference tracking.
    /// </summary>
    public const string CreateReferencesTable = @"
CREATE TABLE IF NOT EXISTS symbol_references (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    from_symbol_id INTEGER NOT NULL,
    to_symbol_id INTEGER NOT NULL,
    reference_kind TEXT NOT NULL,
    file_id INTEGER NOT NULL,
    line INTEGER NOT NULL,
    column INTEGER NOT NULL,
    FOREIGN KEY (from_symbol_id) REFERENCES symbols(id) ON DELETE CASCADE,
    FOREIGN KEY (to_symbol_id) REFERENCES symbols(id) ON DELETE CASCADE,
    FOREIGN KEY (file_id) REFERENCES files(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_refs_from ON symbol_references(from_symbol_id);
CREATE INDEX IF NOT EXISTS idx_refs_to ON symbol_references(to_symbol_id);
CREATE INDEX IF NOT EXISTS idx_refs_file ON symbol_references(file_id);
CREATE INDEX IF NOT EXISTS idx_refs_kind ON symbol_references(reference_kind);";

    /// <summary>
    /// SQL to create the analysis results table.
    /// </summary>
    public const string CreateAnalysisResultsTable = @"
CREATE TABLE IF NOT EXISTS analysis_results (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    file_id INTEGER NOT NULL,
    diagnostic_id TEXT NOT NULL,
    severity TEXT NOT NULL,
    message TEXT NOT NULL,
    line INTEGER NOT NULL,
    column INTEGER NOT NULL,
    cached_at TEXT NOT NULL,
    FOREIGN KEY (file_id) REFERENCES files(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_analysis_file ON analysis_results(file_id);
CREATE INDEX IF NOT EXISTS idx_analysis_severity ON analysis_results(severity);
CREATE INDEX IF NOT EXISTS idx_analysis_diagnostic ON analysis_results(diagnostic_id);";

    /// <summary>
    /// SQL to create the project structure key-value table.
    /// </summary>
    public const string CreateProjectStructureTable = @"
CREATE TABLE IF NOT EXISTS project_structure (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL,
    key TEXT NOT NULL,
    value_json TEXT NOT NULL,
    FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
    UNIQUE(project_id, key)
);

CREATE INDEX IF NOT EXISTS idx_structure_project ON project_structure(project_id);
CREATE INDEX IF NOT EXISTS idx_structure_key ON project_structure(key);";

    /// <summary>
    /// Returns all table creation scripts in dependency order.
    /// </summary>
    public static IReadOnlyList<string> GetAllCreateTableScripts()
    {
        return new[]
        {
            CreateVersionTable,
            CreateProjectsTable,
            CreateFilesTable,
            CreateSymbolsTable,
            CreateReferencesTable,
            CreateAnalysisResultsTable,
            CreateProjectStructureTable
        };
    }

    /// <summary>
    /// SQL to enable foreign keys (must be run per connection).
    /// </summary>
    public const string EnableForeignKeys = "PRAGMA foreign_keys = ON;";

    /// <summary>
    /// SQL to optimize for performance.
    /// </summary>
    public const string OptimizeSettings = @"
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA cache_size = -64000;
PRAGMA temp_store = MEMORY;";
}
