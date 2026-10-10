using System.Data;
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

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("ConnectEV")
            ?? "Server=localhost\\SQLEXPRESS;Database=ConnectEV;Integrated Security=True;TrustServerCertificate=True;";
    }

    public IDbConnection CreateConnection()
    {
        return new SqlConnection(_connectionString);
    }

    public async Task<SqlConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
