using MCPsharp.Models.Database;
using Microsoft.Data.Sqlite;

namespace MCPsharp.Services.Database;

public sealed partial class ProjectDatabase
{
    /// <summary>
    /// Inserts or updates a symbol.
    /// </summary>
    public async Task<long> UpsertSymbolAsync(DbSymbol symbol, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO symbols (file_id, name, kind, namespace, containing_type, line, column, end_line, end_column, accessibility, signature)
            VALUES ($fileId, $name, $kind, $namespace, $containingType, $line, $column, $endLine, $endColumn, $accessibility, $signature)
            RETURNING id";

        cmd.Parameters.AddWithValue("$fileId", symbol.FileId);
        cmd.Parameters.AddWithValue("$name", symbol.Name);
        cmd.Parameters.AddWithValue("$kind", symbol.Kind);
        cmd.Parameters.AddWithValue("$namespace", symbol.Namespace ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$containingType", symbol.ContainingType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$line", symbol.Line);
        cmd.Parameters.AddWithValue("$column", symbol.Column);
        cmd.Parameters.AddWithValue("$endLine", symbol.EndLine);
        cmd.Parameters.AddWithValue("$endColumn", symbol.EndColumn);
        cmd.Parameters.AddWithValue("$accessibility", symbol.Accessibility);
        cmd.Parameters.AddWithValue("$signature", symbol.Signature ?? (object)DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    /// <summary>
    /// Bulk inserts symbols efficiently within a transaction.
    /// </summary>
    public async Task UpsertSymbolsBatchAsync(IEnumerable<DbSymbol> symbols, CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync(async (conn, transaction) =>
        {
            foreach (var symbol in symbols)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO symbols (file_id, name, kind, namespace, containing_type, line, column, end_line, end_column, accessibility, signature)
                    VALUES ($fileId, $name, $kind, $namespace, $containingType, $line, $column, $endLine, $endColumn, $accessibility, $signature)";

                cmd.Parameters.AddWithValue("$fileId", symbol.FileId);
                cmd.Parameters.AddWithValue("$name", symbol.Name);
                cmd.Parameters.AddWithValue("$kind", symbol.Kind);
                cmd.Parameters.AddWithValue("$namespace", symbol.Namespace ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$containingType", symbol.ContainingType ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$line", symbol.Line);
                cmd.Parameters.AddWithValue("$column", symbol.Column);
                cmd.Parameters.AddWithValue("$endLine", symbol.EndLine);
                cmd.Parameters.AddWithValue("$endColumn", symbol.EndColumn);
                cmd.Parameters.AddWithValue("$accessibility", symbol.Accessibility);
                cmd.Parameters.AddWithValue("$signature", symbol.Signature ?? (object)DBNull.Value);

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds symbols by name with optional kind filter.
    /// </summary>
    public async Task<IReadOnlyList<DbSymbol>> FindSymbolsByNameAsync(
        string name,
        string? kind = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var symbols = new List<DbSymbol>();

        await using var cmd = conn.CreateCommand();

        var sql = @"
            SELECT id, file_id, name, kind, namespace, containing_type, line, column, end_line, end_column, accessibility, signature
            FROM symbols
            WHERE name LIKE $pattern";

        if (kind != null)
        {
            sql += " AND kind = $kind";
        }

        sql += " ORDER BY name LIMIT $limit";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$pattern", $"%{name}%");
        if (kind != null)
        {
            cmd.Parameters.AddWithValue("$kind", kind);
        }
        cmd.Parameters.AddWithValue("$limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            symbols.Add(MapToDbSymbol(reader));
        }
        return symbols;
    }

    /// <summary>
    /// Finds symbols by exact name match.
    /// </summary>
    public async Task<IReadOnlyList<DbSymbol>> FindSymbolsByExactNameAsync(
        string name,
        string? kind = null,
        CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var symbols = new List<DbSymbol>();

        await using var cmd = conn.CreateCommand();

        var sql = @"
            SELECT id, file_id, name, kind, namespace, containing_type, line, column, end_line, end_column, accessibility, signature
            FROM symbols
            WHERE name = $name";

        if (kind != null)
        {
            sql += " AND kind = $kind";
        }

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$name", name);
        if (kind != null)
        {
            cmd.Parameters.AddWithValue("$kind", kind);
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            symbols.Add(MapToDbSymbol(reader));
        }
        return symbols;
    }

    /// <summary>
    /// Gets all symbols in a file.
    /// </summary>
    public async Task<IReadOnlyList<DbSymbol>> GetSymbolsInFileAsync(long fileId, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var symbols = new List<DbSymbol>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, file_id, name, kind, namespace, containing_type, line, column, end_line, end_column, accessibility, signature
            FROM symbols
            WHERE file_id = $fileId
            ORDER BY line, column";

        cmd.Parameters.AddWithValue("$fileId", fileId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            symbols.Add(MapToDbSymbol(reader));
        }
        return symbols;
    }

    /// <summary>
    /// Deletes all symbols for a file.
    /// </summary>
    public async Task DeleteSymbolsForFileAsync(long fileId, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM symbols WHERE file_id = $fileId";
        cmd.Parameters.AddWithValue("$fileId", fileId);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Searches symbols with multiple filters.
    /// </summary>
    public async Task<IReadOnlyList<DbSymbol>> SearchSymbolsAsync(
        string? query = null,
        IEnumerable<string>? kinds = null,
        IEnumerable<string>? namespaces = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var symbols = new List<DbSymbol>();

        await using var cmd = conn.CreateCommand();

        var conditions = new List<string>();

        if (!string.IsNullOrEmpty(query))
        {
            conditions.Add("name LIKE $query");
            cmd.Parameters.AddWithValue("$query", $"%{query}%");
        }

        var kindList = kinds?.ToList();
        if (kindList?.Count > 0)
        {
            var kindParams = new List<string>();
            for (var i = 0; i < kindList.Count; i++)
            {
                var paramName = $"$kind{i}";
                kindParams.Add(paramName);
                cmd.Parameters.AddWithValue(paramName, kindList[i]);
            }
            conditions.Add($"kind IN ({string.Join(", ", kindParams)})");
        }

        var namespaceList = namespaces?.ToList();
        if (namespaceList?.Count > 0)
        {
            var nsParams = new List<string>();
            for (var i = 0; i < namespaceList.Count; i++)
            {
                var paramName = $"$ns{i}";
                nsParams.Add(paramName);
                cmd.Parameters.AddWithValue(paramName, namespaceList[i]);
            }
            conditions.Add($"namespace IN ({string.Join(", ", nsParams)})");
        }

        var sql = @"
            SELECT id, file_id, name, kind, namespace, containing_type, line, column, end_line, end_column, accessibility, signature
            FROM symbols";

        if (conditions.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", conditions);
        }

        sql += " ORDER BY name LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            symbols.Add(MapToDbSymbol(reader));
        }
        return symbols;
    }

    /// <summary>
    /// Gets symbol count by kind.
    /// </summary>
    public async Task<Dictionary<string, int>> GetSymbolCountsByKindAsync(CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var counts = new Dictionary<string, int>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT kind, COUNT(*) as count
            FROM symbols
            GROUP BY kind
            ORDER BY count DESC";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }
        return counts;
    }

    /// <summary>
    /// Gets a symbol by ID.
    /// </summary>
    public async Task<DbSymbol?> GetSymbolByIdAsync(long symbolId, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, file_id, name, kind, namespace, containing_type, line, column, end_line, end_column, accessibility, signature
            FROM symbols
            WHERE id = $symbolId";

        cmd.Parameters.AddWithValue("$symbolId", symbolId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MapToDbSymbol(reader);
        }
        return null;
    }

    private static DbSymbol MapToDbSymbol(SqliteDataReader reader)
    {
        return new DbSymbol
        {
            Id = reader.GetInt64(0),
            FileId = reader.GetInt64(1),
            Name = reader.GetString(2),
            Kind = reader.GetString(3),
            Namespace = reader.IsDBNull(4) ? null : reader.GetString(4),
            ContainingType = reader.IsDBNull(5) ? null : reader.GetString(5),
            Line = reader.GetInt32(6),
            Column = reader.GetInt32(7),
            EndLine = reader.GetInt32(8),
            EndColumn = reader.GetInt32(9),
            Accessibility = reader.GetString(10),
            Signature = reader.IsDBNull(11) ? null : reader.GetString(11)
        };
    }
}
