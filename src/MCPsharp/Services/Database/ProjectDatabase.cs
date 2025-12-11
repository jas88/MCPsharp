using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Database;

/// <summary>
/// Manages the SQLite project cache database.
/// Database is stored at ~/.cache/mcpsharp/{project-hash}.db
/// </summary>
public sealed partial class ProjectDatabase : IAsyncDisposable
{
    private readonly ILogger<ProjectDatabase>? _logger;
    private SqliteConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public string? DatabasePath { get; private set; }
    public bool IsOpen => _connection?.State == System.Data.ConnectionState.Open;

    public ProjectDatabase(ILogger<ProjectDatabase>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Opens or creates a database for the specified project root.
    /// </summary>
    public async Task OpenOrCreateAsync(string projectRootPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection != null)
            {
                await CloseInternalAsync().ConfigureAwait(false);
            }

            DatabasePath = GetDatabasePath(projectRootPath);
            EnsureCacheDirectoryExists();

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            _connection = new SqliteConnection(connectionString);
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await InitializeSchemaAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Opened project database: {Path}", DatabasePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Opens an in-memory database (for testing).
    /// </summary>
    public async Task OpenInMemoryAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection != null)
            {
                await CloseInternalAsync().ConfigureAwait(false);
            }

            DatabasePath = ":memory:";

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:",
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            _connection = new SqliteConnection(connectionString);
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await InitializeSchemaAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug("Opened in-memory database");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Closes the database connection.
    /// </summary>
    public async Task CloseAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await CloseInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task CloseInternalAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
            _logger?.LogDebug("Closed database connection");
        }
    }

    private async Task InitializeSchemaAsync(CancellationToken cancellationToken)
    {
        if (_connection == null) throw new InvalidOperationException("Database not open");

        // Enable foreign keys
        await using var fkCmd = _connection.CreateCommand();
        fkCmd.CommandText = ProjectDatabaseSchema.EnableForeignKeys;
        await fkCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // Apply optimization settings
        await using var optCmd = _connection.CreateCommand();
        optCmd.CommandText = ProjectDatabaseSchema.OptimizeSettings;
        await optCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // Check current schema version
        var currentVersion = await GetSchemaVersionAsync(cancellationToken).ConfigureAwait(false);

        if (currentVersion < ProjectDatabaseSchema.CurrentSchemaVersion)
        {
            // Create all tables
            foreach (var createScript in ProjectDatabaseSchema.GetAllCreateTableScripts())
            {
                await using var cmd = _connection.CreateCommand();
                cmd.CommandText = createScript;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Update version
            await SetSchemaVersionAsync(ProjectDatabaseSchema.CurrentSchemaVersion, cancellationToken)
                .ConfigureAwait(false);

            _logger?.LogInformation("Initialized database schema to version {Version}",
                ProjectDatabaseSchema.CurrentSchemaVersion);
        }
    }

    private async Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken)
    {
        if (_connection == null) return 0;

        try
        {
            // First check if table exists
            await using var checkCmd = _connection.CreateCommand();
            checkCmd.CommandText = @"
                SELECT name FROM sqlite_master
                WHERE type='table' AND name='schema_version'";
            var exists = await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (exists == null) return 0;

            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT MAX(version) FROM schema_version";
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result == DBNull.Value || result == null ? 0 : Convert.ToInt32(result);
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    private async Task SetSchemaVersionAsync(int version, CancellationToken cancellationToken)
    {
        if (_connection == null) return;

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO schema_version (version, applied_at)
            VALUES ($version, $appliedAt)";
        cmd.Parameters.AddWithValue("$version", version);
        cmd.Parameters.AddWithValue("$appliedAt", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the database file path for a project.
    /// </summary>
    public static string GetDatabasePath(string projectRootPath)
    {
        var hash = ComputeProjectHash(projectRootPath);
        var cacheDir = GetCacheDirectory();
        return Path.Combine(cacheDir, $"{hash}.db");
    }

    /// <summary>
    /// Gets the cache directory path.
    /// </summary>
    public static string GetCacheDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".cache", "mcpsharp");
    }

    private static void EnsureCacheDirectoryExists()
    {
        var cacheDir = GetCacheDirectory();
        if (!Directory.Exists(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }
    }

    /// <summary>
    /// Computes a deterministic hash for a project path.
    /// </summary>
    public static string ComputeProjectHash(string projectPath)
    {
        var canonicalPath = Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar);
        var bytes = Encoding.UTF8.GetBytes(canonicalPath.ToLowerInvariant());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Gets the internal connection for advanced operations.
    /// </summary>
    internal SqliteConnection GetConnection()
    {
        return _connection ?? throw new InvalidOperationException("Database not open");
    }

    /// <summary>
    /// Executes an action within a transaction.
    /// </summary>
    public async Task ExecuteInTransactionAsync(
        Func<SqliteConnection, SqliteTransaction, Task> action,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var conn = GetConnection();
            await using var transaction = conn.BeginTransaction();
            try
            {
                await action(conn, transaction).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await CloseAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}
