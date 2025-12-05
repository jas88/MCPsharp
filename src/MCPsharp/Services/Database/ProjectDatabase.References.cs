using MCPsharp.Models.Database;
using Microsoft.Data.Sqlite;

namespace MCPsharp.Services.Database;

public sealed partial class ProjectDatabase
{
    /// <summary>
    /// Direction for call chain analysis.
    /// </summary>
    public enum CallChainDirection
    {
        /// <summary>Who calls this symbol (backward trace)</summary>
        Backward,
        /// <summary>What does this symbol call (forward trace)</summary>
        Forward
    }

    /// <summary>
    /// Inserts a reference.
    /// </summary>
    public async Task<long> UpsertReferenceAsync(DbReference reference, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO symbol_references (from_symbol_id, to_symbol_id, reference_kind, file_id, line, column)
            VALUES ($fromSymbolId, $toSymbolId, $referenceKind, $fileId, $line, $column)
            RETURNING id";

        cmd.Parameters.AddWithValue("$fromSymbolId", reference.FromSymbolId);
        cmd.Parameters.AddWithValue("$toSymbolId", reference.ToSymbolId);
        cmd.Parameters.AddWithValue("$referenceKind", reference.ReferenceKind);
        cmd.Parameters.AddWithValue("$fileId", reference.FileId);
        cmd.Parameters.AddWithValue("$line", reference.Line);
        cmd.Parameters.AddWithValue("$column", reference.Column);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    /// <summary>
    /// Bulk inserts references within a transaction.
    /// </summary>
    public async Task UpsertReferencesBatchAsync(IEnumerable<DbReference> references, CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync(async (conn, transaction) =>
        {
            foreach (var reference in references)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO symbol_references (from_symbol_id, to_symbol_id, reference_kind, file_id, line, column)
                    VALUES ($fromSymbolId, $toSymbolId, $referenceKind, $fileId, $line, $column)";

                cmd.Parameters.AddWithValue("$fromSymbolId", reference.FromSymbolId);
                cmd.Parameters.AddWithValue("$toSymbolId", reference.ToSymbolId);
                cmd.Parameters.AddWithValue("$referenceKind", reference.ReferenceKind);
                cmd.Parameters.AddWithValue("$fileId", reference.FileId);
                cmd.Parameters.AddWithValue("$line", reference.Line);
                cmd.Parameters.AddWithValue("$column", reference.Column);

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds all callers of a symbol (who calls this).
    /// </summary>
    public async Task<IReadOnlyList<(DbReference Reference, DbSymbol Caller)>> FindCallersAsync(
        long symbolId,
        CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var results = new List<(DbReference, DbSymbol)>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                r.id, r.from_symbol_id, r.to_symbol_id, r.reference_kind, r.file_id, r.line, r.column,
                s.id, s.file_id, s.name, s.kind, s.namespace, s.containing_type, s.line, s.column, s.end_line, s.end_column, s.accessibility, s.signature
            FROM symbol_references r
            JOIN symbols s ON r.from_symbol_id = s.id
            WHERE r.to_symbol_id = $symbolId
            ORDER BY s.name";

        cmd.Parameters.AddWithValue("$symbolId", symbolId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var reference = new DbReference
            {
                Id = reader.GetInt64(0),
                FromSymbolId = reader.GetInt64(1),
                ToSymbolId = reader.GetInt64(2),
                ReferenceKind = reader.GetString(3),
                FileId = reader.GetInt64(4),
                Line = reader.GetInt32(5),
                Column = reader.GetInt32(6)
            };

            var symbol = new DbSymbol
            {
                Id = reader.GetInt64(7),
                FileId = reader.GetInt64(8),
                Name = reader.GetString(9),
                Kind = reader.GetString(10),
                Namespace = reader.IsDBNull(11) ? null : reader.GetString(11),
                ContainingType = reader.IsDBNull(12) ? null : reader.GetString(12),
                Line = reader.GetInt32(13),
                Column = reader.GetInt32(14),
                EndLine = reader.GetInt32(15),
                EndColumn = reader.GetInt32(16),
                Accessibility = reader.GetString(17),
                Signature = reader.IsDBNull(18) ? null : reader.GetString(18)
            };

            results.Add((reference, symbol));
        }
        return results;
    }

    /// <summary>
    /// Finds all callees of a symbol (what does this call).
    /// </summary>
    public async Task<IReadOnlyList<(DbReference Reference, DbSymbol Callee)>> FindCalleesAsync(
        long symbolId,
        CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var results = new List<(DbReference, DbSymbol)>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                r.id, r.from_symbol_id, r.to_symbol_id, r.reference_kind, r.file_id, r.line, r.column,
                s.id, s.file_id, s.name, s.kind, s.namespace, s.containing_type, s.line, s.column, s.end_line, s.end_column, s.accessibility, s.signature
            FROM symbol_references r
            JOIN symbols s ON r.to_symbol_id = s.id
            WHERE r.from_symbol_id = $symbolId
            ORDER BY r.line, r.column";

        cmd.Parameters.AddWithValue("$symbolId", symbolId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var reference = new DbReference
            {
                Id = reader.GetInt64(0),
                FromSymbolId = reader.GetInt64(1),
                ToSymbolId = reader.GetInt64(2),
                ReferenceKind = reader.GetString(3),
                FileId = reader.GetInt64(4),
                Line = reader.GetInt32(5),
                Column = reader.GetInt32(6)
            };

            var symbol = new DbSymbol
            {
                Id = reader.GetInt64(7),
                FileId = reader.GetInt64(8),
                Name = reader.GetString(9),
                Kind = reader.GetString(10),
                Namespace = reader.IsDBNull(11) ? null : reader.GetString(11),
                ContainingType = reader.IsDBNull(12) ? null : reader.GetString(12),
                Line = reader.GetInt32(13),
                Column = reader.GetInt32(14),
                EndLine = reader.GetInt32(15),
                EndColumn = reader.GetInt32(16),
                Accessibility = reader.GetString(17),
                Signature = reader.IsDBNull(18) ? null : reader.GetString(18)
            };

            results.Add((reference, symbol));
        }
        return results;
    }

    /// <summary>
    /// Gets a call chain using recursive CTE.
    /// </summary>
    public async Task<IReadOnlyList<DbSymbol>> GetCallChainAsync(
        long symbolId,
        CallChainDirection direction,
        int maxDepth = 10,
        CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var symbols = new List<DbSymbol>();

        await using var cmd = conn.CreateCommand();

        // Use recursive CTE for call chain analysis
        var (fromCol, toCol) = direction == CallChainDirection.Backward
            ? ("to_symbol_id", "from_symbol_id")
            : ("from_symbol_id", "to_symbol_id");

        cmd.CommandText = $@"
            WITH RECURSIVE call_chain(symbol_id, depth) AS (
                SELECT $startId, 0
                UNION ALL
                SELECT r.{toCol}, cc.depth + 1
                FROM symbol_references r
                JOIN call_chain cc ON r.{fromCol} = cc.symbol_id
                WHERE cc.depth < $maxDepth
            )
            SELECT DISTINCT s.id, s.file_id, s.name, s.kind, s.namespace, s.containing_type, s.line, s.column, s.end_line, s.end_column, s.accessibility, s.signature
            FROM call_chain cc
            JOIN symbols s ON cc.symbol_id = s.id
            WHERE cc.symbol_id != $startId
            ORDER BY s.name";

        cmd.Parameters.AddWithValue("$startId", symbolId);
        cmd.Parameters.AddWithValue("$maxDepth", maxDepth);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            symbols.Add(new DbSymbol
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
            });
        }
        return symbols;
    }

    /// <summary>
    /// Deletes all references for a file.
    /// </summary>
    public async Task DeleteReferencesForFileAsync(long fileId, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM symbol_references WHERE file_id = $fileId";
        cmd.Parameters.AddWithValue("$fileId", fileId);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets reference count by kind.
    /// </summary>
    public async Task<Dictionary<string, int>> GetReferenceCountsByKindAsync(CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var counts = new Dictionary<string, int>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT reference_kind, COUNT(*) as count
            FROM symbol_references
            GROUP BY reference_kind
            ORDER BY count DESC";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }
        return counts;
    }

    /// <summary>
    /// Finds all references to a symbol (by any symbol).
    /// </summary>
    public async Task<IReadOnlyList<DbReference>> GetReferencesToSymbolAsync(long symbolId, CancellationToken cancellationToken = default)
    {
        var conn = GetConnection();
        var references = new List<DbReference>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, from_symbol_id, to_symbol_id, reference_kind, file_id, line, column
            FROM symbol_references
            WHERE to_symbol_id = $symbolId
            ORDER BY file_id, line, column";

        cmd.Parameters.AddWithValue("$symbolId", symbolId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            references.Add(MapToDbReference(reader));
        }
        return references;
    }

    private static DbReference MapToDbReference(SqliteDataReader reader)
    {
        return new DbReference
        {
            Id = reader.GetInt64(0),
            FromSymbolId = reader.GetInt64(1),
            ToSymbolId = reader.GetInt64(2),
            ReferenceKind = reader.GetString(3),
            FileId = reader.GetInt64(4),
            Line = reader.GetInt32(5),
            Column = reader.GetInt32(6)
        };
    }
}
