using SqlSugar;

namespace Qizhu.Infrastructure.Database;

/// <summary>每个请求拥有独立客户端；业务模块不得跨请求共享过滤器或事务。</summary>
public sealed class DatabaseConnection : IDatabaseConnection
{
    public DatabaseConnection(string connectionString)
    {
        Client = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });
        Client.Ado.CommandTimeOut = 15;
    }

    public SqlSugarClient Client { get; }
    public void Dispose() => Client.Dispose();
}

/// <summary>请求范围的数据库会话；仓储、审计和幂等共享此会话以保证事务一致性。</summary>
public interface IDatabaseConnection : IDisposable
{
    SqlSugarClient Client { get; }
}
