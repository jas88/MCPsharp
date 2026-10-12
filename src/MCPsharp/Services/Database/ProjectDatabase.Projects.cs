using System.Globalization;
using MCPsharp.Models.Database;
using Microsoft.Data.Sqlite;

namespace MCPsharp.Services.Database;

public sealed partial class ProjectDatabase
{
    /// <summary>
    /// Gets or creates a project record.
    /// </summary>
    public async Task<long> GetOrCreateProjectAsync(
        string rootPath,
        string name,
        CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var hash = ComputeProjectHash(rootPath);

        // Try to get existing
        await using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT id FROM projects WHERE root_path_hash = $hash";
        selectCmd.Parameters.AddWithValue("$hash", hash);

        var existingId = await selectCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (existingId != null && existingId != DBNull.Value)
        {
            // Update opened_at
            await using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = "UPDATE projects SET opened_at = $openedAt WHERE id = $id";
            updateCmd.Parameters.AddWithValue("$openedAt", DateTime.UtcNow.ToString("O"));
            updateCmd.Parameters.AddWithValue("$id", existingId);
            await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return Convert.ToInt64(existingId, CultureInfo.InvariantCulture);
        }

        // Create new
        await using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO projects (root_path_hash, root_path, name, opened_at)
            VALUES ($hash, $rootPath, $name, $openedAt)
            RETURNING id";

        insertCmd.Parameters.AddWithValue("$hash", hash);
        insertCmd.Parameters.AddWithValue("$rootPath", rootPath);
        insertCmd.Parameters.AddWithValue("$name", name);
        insertCmd.Parameters.AddWithValue("$openedAt", DateTime.UtcNow.ToString("O"));

        var result = await insertCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets a project by ID.
    /// </summary>
    public async Task<DbProject?> GetProjectByIdAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, root_path_hash, root_path, name, opened_at, solution_count, project_count, file_count
            FROM projects
            WHERE id = $projectId";

        cmd.Parameters.AddWithValue("$projectId", projectId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MapToDbProject(reader);
        }
        return null;
    }

    /// <summary>
    /// Gets a project by root path hash.
    /// </summary>
    public async Task<DbProject?> GetProjectByPathAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var hash = ComputeProjectHash(rootPath);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, root_path_hash, root_path, name, opened_at, solution_count, project_count, file_count
            FROM projects
            WHERE root_path_hash = $hash";

        cmd.Parameters.AddWithValue("$hash", hash);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MapToDbProject(reader);
        }
        return null;
    }

    private static readonly (string Table, string CountQuery)[] TableCountQueries =
    [
        ("projects", "SELECT COUNT(*) FROM projects"),
        ("files", "SELECT COUNT(*) FROM files"),
        ("symbols", "SELECT COUNT(*) FROM symbols"),
        ("symbol_references", "SELECT COUNT(*) FROM symbol_references"),
        ("analysis_results", "SELECT COUNT(*) FROM analysis_results"),
    ];

    /// <summary>
    /// Updates project statistics.
    /// </summary>
    public async Task UpdateProjectStatsAsync(
        long projectId,
        int? solutionCount = null,
        int? projectCount = null,
        int? fileCount = null,
        CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        if (!solutionCount.HasValue && !projectCount.HasValue && !fileCount.HasValue)
            return;

        await using var cmd = conn.CreateCommand();

        // NULL parameters leave the existing column value unchanged
        cmd.CommandText = """
            UPDATE projects SET
                solution_count = COALESCE($solutionCount, solution_count),
                project_count = COALESCE($projectCount, project_count),
                file_count = COALESCE($fileCount, file_count)
            WHERE id = $projectId
            """;
        cmd.Parameters.AddWithValue("$solutionCount", (object?)solutionCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$projectCount", (object?)projectCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fileCount", (object?)fileCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$projectId", projectId);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stores project structure data (flexible key-value).
    /// </summary>
    public async Task SetProjectStructureAsync(
        long projectId,
        string key,
        string valueJson,
        CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO project_structure (project_id, key, value_json)
            VALUES ($projectId, $key, $valueJson)
            ON CONFLICT(project_id, key) DO UPDATE SET value_json = excluded.value_json";

        cmd.Parameters.AddWithValue("$projectId", projectId);
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$valueJson", valueJson);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets project structure data.
    /// </summary>
    public async Task<string?> GetProjectStructureAsync(
        long projectId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT value_json FROM project_structure
            WHERE project_id = $projectId AND key = $key";

        cmd.Parameters.AddWithValue("$projectId", projectId);
        cmd.Parameters.AddWithValue("$key", key);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result == null || result == DBNull.Value ? null : (string)result;
    }

    /// <summary>
    /// Gets all project structure keys.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetProjectStructureKeysAsync(
        long projectId,
        CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var keys = new List<string>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key FROM project_structure WHERE project_id = $projectId ORDER BY key";
        cmd.Parameters.AddWithValue("$projectId", projectId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(reader.GetString(0));
        }
        return keys;
    }

    /// <summary>
    /// Deletes a project and all associated data.
    /// </summary>
    public async Task DeleteProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        // Foreign key cascade will handle files, symbols, references
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM projects WHERE id = $projectId";
        cmd.Parameters.AddWithValue("$projectId", projectId);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets summary statistics for the database.
    /// </summary>
    public async Task<Dictionary<string, long>> GetDatabaseStatsAsync(CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var stats = new Dictionary<string, long>();

        foreach (var (table, countQuery) in TableCountQueries)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = countQuery;
            var count = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            stats[table] = Convert.ToInt64(count, CultureInfo.InvariantCulture);
        }

        return stats;
    }

    private static DbProject MapToDbProject(SqliteDataReader reader)
    {
        return new DbProject
        {
            Id = reader.GetInt64(0),
            RootPathHash = reader.GetString(1),
            RootPath = reader.GetString(2),
            Name = reader.GetString(3),
            OpenedAt = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            SolutionCount = reader.GetInt32(5),
            ProjectCount = reader.GetInt32(6),
            FileCount = reader.GetInt32(7)
        };
    }
}
