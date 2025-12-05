using MCPsharp.Models.Database;
using Microsoft.Data.Sqlite;

namespace MCPsharp.Services.Database;

public sealed partial class ProjectDatabase
{
    /// <summary>
    /// Inserts or updates a file record.
    /// </summary>
    public async Task<long> UpsertFileAsync(
        long projectId,
        string relativePath,
        string contentHash,
        long sizeBytes,
        string language,
        CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO files (project_id, relative_path, content_hash, last_indexed, size_bytes, language)
            VALUES ($projectId, $relativePath, $contentHash, $lastIndexed, $sizeBytes, $language)
            ON CONFLICT(project_id, relative_path) DO UPDATE SET
                content_hash = excluded.content_hash,
                last_indexed = excluded.last_indexed,
                size_bytes = excluded.size_bytes,
                language = excluded.language
            RETURNING id";

        cmd.Parameters.AddWithValue("$projectId", projectId);
        cmd.Parameters.AddWithValue("$relativePath", relativePath);
        cmd.Parameters.AddWithValue("$contentHash", contentHash);
        cmd.Parameters.AddWithValue("$lastIndexed", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$sizeBytes", sizeBytes);
        cmd.Parameters.AddWithValue("$language", language);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    /// <summary>
    /// Gets a file by relative path.
    /// </summary>
    public async Task<DbFile?> GetFileAsync(long projectId, string relativePath, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, project_id, relative_path, content_hash, last_indexed, size_bytes, language
            FROM files
            WHERE project_id = $projectId AND relative_path = $relativePath";

        cmd.Parameters.AddWithValue("$projectId", projectId);
        cmd.Parameters.AddWithValue("$relativePath", relativePath);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MapToDbFile(reader);
        }
        return null;
    }

    /// <summary>
    /// Gets all files for a project.
    /// </summary>
    public async Task<IReadOnlyList<DbFile>> GetAllFilesAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var files = new List<DbFile>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, project_id, relative_path, content_hash, last_indexed, size_bytes, language
            FROM files
            WHERE project_id = $projectId
            ORDER BY relative_path";

        cmd.Parameters.AddWithValue("$projectId", projectId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            files.Add(MapToDbFile(reader));
        }
        return files;
    }

    /// <summary>
    /// Gets files that need re-indexing based on changed content hashes.
    /// </summary>
    public async Task<IReadOnlyList<DbFile>> GetStaleFilesAsync(
        long projectId,
        Dictionary<string, string> currentHashes,
        CancellationToken cancellationToken = default)
    {
        var allFiles = await GetAllFilesAsync(projectId, cancellationToken).ConfigureAwait(false);
        var staleFiles = new List<DbFile>();

        foreach (var file in allFiles)
        {
            if (!currentHashes.TryGetValue(file.RelativePath, out var currentHash) ||
                currentHash != file.ContentHash)
            {
                staleFiles.Add(file);
            }
        }

        return staleFiles;
    }

    /// <summary>
    /// Gets files that exist in the database but not in the current file set.
    /// </summary>
    public async Task<IReadOnlyList<DbFile>> GetDeletedFilesAsync(
        long projectId,
        HashSet<string> currentPaths,
        CancellationToken cancellationToken = default)
    {
        var allFiles = await GetAllFilesAsync(projectId, cancellationToken).ConfigureAwait(false);
        return allFiles.Where(f => !currentPaths.Contains(f.RelativePath)).ToList();
    }

    /// <summary>
    /// Deletes a file and its associated symbols and references.
    /// </summary>
    public async Task DeleteFileAsync(long fileId, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        // Foreign key cascade will handle symbols and references
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM files WHERE id = $fileId";
        cmd.Parameters.AddWithValue("$fileId", fileId);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a file by path.
    /// </summary>
    public async Task DeleteFileByPathAsync(long projectId, string relativePath, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM files WHERE project_id = $projectId AND relative_path = $relativePath";
        cmd.Parameters.AddWithValue("$projectId", projectId);
        cmd.Parameters.AddWithValue("$relativePath", relativePath);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets file count for a project.
    /// </summary>
    public async Task<int> GetFileCountAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM files WHERE project_id = $projectId";
        cmd.Parameters.AddWithValue("$projectId", projectId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Bulk upserts files efficiently.
    /// </summary>
    public async Task UpsertFilesBatchAsync(
        long projectId,
        IEnumerable<(string RelativePath, string ContentHash, long SizeBytes, string Language)> files,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync(async (conn, transaction) =>
        {
            foreach (var file in files)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO files (project_id, relative_path, content_hash, last_indexed, size_bytes, language)
                    VALUES ($projectId, $relativePath, $contentHash, $lastIndexed, $sizeBytes, $language)
                    ON CONFLICT(project_id, relative_path) DO UPDATE SET
                        content_hash = excluded.content_hash,
                        last_indexed = excluded.last_indexed,
                        size_bytes = excluded.size_bytes,
                        language = excluded.language";

                cmd.Parameters.AddWithValue("$projectId", projectId);
                cmd.Parameters.AddWithValue("$relativePath", file.RelativePath);
                cmd.Parameters.AddWithValue("$contentHash", file.ContentHash);
                cmd.Parameters.AddWithValue("$lastIndexed", DateTime.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("$sizeBytes", file.SizeBytes);
                cmd.Parameters.AddWithValue("$language", file.Language);

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static DbFile MapToDbFile(SqliteDataReader reader)
    {
        return new DbFile
        {
            Id = reader.GetInt64(0),
            ProjectId = reader.GetInt64(1),
            RelativePath = reader.GetString(2),
            ContentHash = reader.GetString(3),
            LastIndexed = DateTime.Parse(reader.GetString(4)),
            SizeBytes = reader.GetInt64(5),
            Language = reader.GetString(6)
        };
    }
}
