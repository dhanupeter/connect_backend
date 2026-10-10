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
        var configured = configuration.GetConnectionString("ConnectEV");
        if (!string.IsNullOrWhiteSpace(configured) && !configured.Contains("localhost"))
        {
            _connectionString = configured;
            _canConnect = true;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Running locally on Windows machine with SQL Express
            _connectionString = configured ?? "Server=localhost\\SQLEXPRESS;Database=ConnectEV;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=3;";
            _canConnect = true;
        }
        else
        {
            // Running inside Linux Docker container on cloud (Render) without an external SQL Server
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
