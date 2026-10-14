using System.Collections.Concurrent;
using ConnectBackend.Data;
using ConnectBackend.Models;
using Dapper;

namespace ConnectBackend.Services;

public interface IActivityLogger
{
    Task<ActivityLog> LogAsync(CreateActivityLogDto dto);
    Task<ActivityLog> LogSuccessAsync(string action, string category, string message, string? userId = null, string? correlationId = null);
    Task<ActivityLog> LogWarningAsync(string action, string category, string message, string? userId = null, string? correlationId = null);
    Task<ActivityLog> LogErrorAsync(string action, string category, string message, string? exceptionDetails = null, string? userId = null, string? correlationId = null);
    Task<ActivityLog> LogExceptionAsync(string action, string category, Exception ex, string? userId = null, string? correlationId = null);
    Task<IReadOnlyList<ActivityLog>> GetLogsAsync(string? status = null, string? category = null, string? action = null, string? userId = null, string? correlationId = null, int limit = 100);
    Task<IDictionary<string, int>> GetStatsAsync();
    void ClearInMemoryLogs();
}

public class ActivityLogger : IActivityLogger
{
    private static readonly ConcurrentQueue<ActivityLog> _inMemoryLogs = new();
    private static long _autoIncrementId = 0;
    private const int MaxInMemoryCapacity = 5000;

    private readonly IDbConnectionFactory _dbFactory;
    private readonly IBigQueryService _bigQueryService;
    private readonly ILogger<ActivityLogger> _logger;
    private static bool _tableEnsured = false;
    private static readonly object _schemaLock = new();

    public ActivityLogger(IDbConnectionFactory dbFactory, IBigQueryService bigQueryService, ILogger<ActivityLogger> logger)
    {
        _dbFactory = dbFactory;
        _bigQueryService = bigQueryService;
        _logger = logger;
    }

    public async Task<ActivityLog> LogAsync(CreateActivityLogDto dto)
    {
        var log = new ActivityLog
        {
            Id = Interlocked.Increment(ref _autoIncrementId),
            UserId = dto.UserId,
            Action = dto.Action,
            Category = string.IsNullOrWhiteSpace(dto.Category) ? "System" : dto.Category,
            Status = string.IsNullOrWhiteSpace(dto.Status) ? "Success" : dto.Status,
            Message = dto.Message,
            ExceptionDetails = dto.ExceptionDetails,
            CorrelationId = dto.CorrelationId,
            CreatedAtUtc = DateTime.UtcNow
        };

        // 1. Always keep in high-speed thread-safe in-memory buffer
        _inMemoryLogs.Enqueue(log);
        while (_inMemoryLogs.Count > MaxInMemoryCapacity && _inMemoryLogs.TryDequeue(out _)) { }

        // Also write to standard ILogger for container/console observability
        switch (log.Status.ToLowerInvariant())
        {
            case "error":
                _logger.LogError("[ActivityLog][{Category}][{Action}] {Message} (User: {UserId}, Correlation: {Corr})",
                    log.Category, log.Action, log.Message, log.UserId, log.CorrelationId);
                break;
            case "exception":
                _logger.LogError("[ActivityLog][{Category}][{Action}] {Message} | Details: {Details} (User: {UserId})",
                    log.Category, log.Action, log.Message, log.ExceptionDetails, log.UserId);
                break;
            case "warning":
                _logger.LogWarning("[ActivityLog][{Category}][{Action}] {Message} (User: {UserId})",
                    log.Category, log.Action, log.Message, log.UserId);
                break;
            default:
                _logger.LogInformation("[ActivityLog][{Category}][{Action}] {Message} (User: {UserId})",
                    log.Category, log.Action, log.Message, log.UserId);
                break;
        }

        // 2. Stream into Google Cloud BigQuery activity_logs table
        _ = _bigQueryService.InsertActivityLogAsync(log);

        // 3. Persist to SQL Server if configured
        _ = PersistToDatabaseAsync(log);

        return log;
    }

    public Task<ActivityLog> LogSuccessAsync(string action, string category, string message, string? userId = null, string? correlationId = null)
    {
        return LogAsync(new CreateActivityLogDto
        {
            Action = action,
            Category = category,
            Status = "Success",
            Message = message,
            UserId = userId,
            CorrelationId = correlationId
        });
    }

    public Task<ActivityLog> LogWarningAsync(string action, string category, string message, string? userId = null, string? correlationId = null)
    {
        return LogAsync(new CreateActivityLogDto
        {
            Action = action,
            Category = category,
            Status = "Warning",
            Message = message,
            UserId = userId,
            CorrelationId = correlationId
        });
    }

    public Task<ActivityLog> LogErrorAsync(string action, string category, string message, string? exceptionDetails = null, string? userId = null, string? correlationId = null)
    {
        return LogAsync(new CreateActivityLogDto
        {
            Action = action,
            Category = category,
            Status = "Error",
            Message = message,
            ExceptionDetails = exceptionDetails,
            UserId = userId,
            CorrelationId = correlationId
        });
    }

    public Task<ActivityLog> LogExceptionAsync(string action, string category, Exception ex, string? userId = null, string? correlationId = null)
    {
        return LogAsync(new CreateActivityLogDto
        {
            Action = action,
            Category = category,
            Status = "Exception",
            Message = ex.Message,
            ExceptionDetails = ex.ToString(),
            UserId = userId,
            CorrelationId = correlationId
        });
    }

    public async Task<IReadOnlyList<ActivityLog>> GetLogsAsync(
        string? status = null,
        string? category = null,
        string? action = null,
        string? userId = null,
        string? correlationId = null,
        int limit = 100)
    {
        if (limit <= 0) limit = 100;
        if (limit > 1000) limit = 1000;

        // Try querying from SQL database first if available
        try
        {
            using var conn = _dbFactory.CreateConnection();
            var sql = @"
                SELECT TOP (@Limit)
                    Id, UserId, Action, Category, Status, Message, ExceptionDetails, CorrelationId, CreatedAtUtc
                FROM dbo.ActivityLogs
                WHERE 1=1
            ";
            var p = new DynamicParameters();
            p.Add("Limit", limit);

            if (!string.IsNullOrWhiteSpace(status))
            {
                sql += " AND Status = @Status";
                p.Add("Status", status);
            }
            if (!string.IsNullOrWhiteSpace(category))
            {
                sql += " AND Category = @Category";
                p.Add("Category", category);
            }
            if (!string.IsNullOrWhiteSpace(action))
            {
                sql += " AND Action = @Action";
                p.Add("Action", action);
            }
            if (!string.IsNullOrWhiteSpace(userId))
            {
                sql += " AND UserId = @UserId";
                p.Add("UserId", userId);
            }
            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                sql += " AND CorrelationId = @CorrelationId";
                p.Add("CorrelationId", correlationId);
            }

            sql += " ORDER BY CreatedAtUtc DESC";

            var rows = await conn.QueryAsync<ActivityLog>(sql, p);
            var list = rows.ToList();
            if (list.Count > 0)
            {
                return list;
            }
        }
        catch
        {
            // Fall back to BigQuery
            try
            {
                var bqLogs = await _bigQueryService.GetActivityLogsAsync(status, category, limit);
                var bqList = bqLogs.ToList();
                if (bqList.Count > 0)
                {
                    return bqList;
                }
            }
            catch { }
        }

        // In-memory query
        IEnumerable<ActivityLog> query = _inMemoryLogs.Reverse();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(x => string.Equals(x.Action, action, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            query = query.Where(x => string.Equals(x.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase));
        }

        return query.Take(limit).ToList();
    }

    public async Task<IDictionary<string, int>> GetStatsAsync()
    {
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Success"] = 0,
            ["Warning"] = 0,
            ["Error"] = 0,
            ["Exception"] = 0,
            ["Total"] = 0
        };

        try
        {
            using var conn = _dbFactory.CreateConnection();
            const string sql = @"
                SELECT Status, COUNT(*) as [Count]
                FROM dbo.ActivityLogs
                GROUP BY Status
            ";
            var rows = await conn.QueryAsync<(string Status, int Count)>(sql);
            int total = 0;
            foreach (var r in rows)
            {
                stats[r.Status] = r.Count;
                total += r.Count;
            }
            stats["Total"] = total;
            return stats;
        }
        catch
        {
            // In-memory stats
            foreach (var log in _inMemoryLogs)
            {
                if (stats.ContainsKey(log.Status))
                    stats[log.Status]++;
                else
                    stats[log.Status] = 1;
                stats["Total"]++;
            }
            return stats;
        }
    }

    public void ClearInMemoryLogs()
    {
        while (_inMemoryLogs.TryDequeue(out _)) { }
    }

    private async Task PersistToDatabaseAsync(ActivityLog log)
    {
        try
        {
            using var conn = _dbFactory.CreateConnection();

            if (!_tableEnsured)
            {
                lock (_schemaLock)
                {
                    if (!_tableEnsured)
                    {
                        const string ensureTableSql = @"
                            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ActivityLogs' AND schema_id = SCHEMA_ID('dbo'))
                            BEGIN
                                CREATE TABLE dbo.ActivityLogs (
                                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                    UserId NVARCHAR(100) NULL,
                                    Action NVARCHAR(100) NOT NULL,
                                    Category NVARCHAR(50) NOT NULL,
                                    Status NVARCHAR(20) NOT NULL,
                                    Message NVARCHAR(MAX) NOT NULL,
                                    ExceptionDetails NVARCHAR(MAX) NULL,
                                    CorrelationId NVARCHAR(100) NULL,
                                    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                                );
                                CREATE NONCLUSTERED INDEX IX_ActivityLogs_Status ON dbo.ActivityLogs(Status);
                                CREATE NONCLUSTERED INDEX IX_ActivityLogs_Category ON dbo.ActivityLogs(Category);
                                CREATE NONCLUSTERED INDEX IX_ActivityLogs_CreatedAtUtc ON dbo.ActivityLogs(CreatedAtUtc DESC);
                                CREATE NONCLUSTERED INDEX IX_ActivityLogs_UserId ON dbo.ActivityLogs(UserId);
                            END
                        ";
                        conn.Execute(ensureTableSql);
                        _tableEnsured = true;
                    }
                }
            }

            const string insertSql = @"
                INSERT INTO dbo.ActivityLogs (
                    UserId, Action, Category, Status, Message, ExceptionDetails, CorrelationId, CreatedAtUtc
                ) VALUES (
                    @UserId, @Action, @Category, @Status, @Message, @ExceptionDetails, @CorrelationId, @CreatedAtUtc
                );
            ";

            await conn.ExecuteAsync(insertSql, log);
        }
        catch
        {
            // Non-blocking in container in-memory fallback
        }
    }
}
