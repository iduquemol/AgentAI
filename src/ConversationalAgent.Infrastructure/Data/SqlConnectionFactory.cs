using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ConversationalAgent.Infrastructure.Data;

public sealed class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection no está configurada. " +
                "Verifica appsettings.Development.json o las variables de entorno.");
    }

    public IDbConnection Create()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
