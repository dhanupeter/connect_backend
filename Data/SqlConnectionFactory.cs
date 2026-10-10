using System.Data;
using System.Runtime.InteropServices;
using Microsoft.Data.SqlClient;

namespace ConnectBackend.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
    Task<SqlConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}

public class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly bool _canConnect;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("ConnectEV")
            ?? Environment.GetEnvironmentVariable("CONNECTEV_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING");

        if (!string.IsNullOrWhiteSpace(configured) && !configured.Contains("localhost"))
        {
            _connectionString = configured;
            _canConnect = true;
        }
        else
        {
            // Real cloud container mode
            _connectionString = string.Empty;
            _canConnect = false;
        }
    }

    public IDbConnection CreateConnection()
    {
        if (!_canConnect || string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("External SQL Server not configured in container. Operating in high-speed in-memory mode.");
        }
        return new SqlConnection(_connectionString);
    }

    public async Task<SqlConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!_canConnect || string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("External SQL Server not configured in container. Operating in high-speed in-memory mode.");
        }
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
