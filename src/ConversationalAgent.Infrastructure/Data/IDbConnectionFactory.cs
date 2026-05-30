using System.Data;

namespace ConversationalAgent.Infrastructure.Data;

public interface IDbConnectionFactory
{
    /// Crea y abre una nueva conexión a SQL Server.
    /// El llamador es responsable de hacer Dispose.
    IDbConnection Create();
}
