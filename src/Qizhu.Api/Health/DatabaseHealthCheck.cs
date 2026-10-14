using Microsoft.Extensions.Diagnostics.HealthChecks;
using Qizhu.Infrastructure.Database;

namespace Qizhu.Api.Health;

public sealed class DatabaseHealthCheck(IDatabaseConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            connection.Client.Ado.CommandTimeOut = 5;
            await connection.Client.Queryable<SnowflakeNodeRecord>().CountAsync();
            return HealthCheckResult.Healthy();
        }
        catch { return HealthCheckResult.Unhealthy("数据库暂不可用。"); }
    }
}
