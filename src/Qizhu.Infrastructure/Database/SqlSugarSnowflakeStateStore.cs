using Microsoft.Data.SqlClient;
using Qizhu.Infrastructure.Common;
using SqlSugar;

namespace Qizhu.Infrastructure.Database;

/// <summary>节点时间片使用独立短连接提交，不参加业务事务；业务回滚不能回收已分配 ID。</summary>
public sealed class SqlSugarSnowflakeStateStore(string connectionString) : ISnowflakeStateStore
{
    public long ReserveTimestamp(int workerId, long currentMilliseconds)
    {
        using var database = new DatabaseConnection(connectionString);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var record = database.Client.Queryable<SnowflakeNodeRecord>().Single(item => item.WorkerId == workerId);
            if (record is null)
            {
                try
                {
                    database.Client.Insertable(new SnowflakeNodeRecord { WorkerId = workerId, LastTimestamp = currentMilliseconds }).ExecuteCommand();
                    return currentMilliseconds;
                }
                catch (Exception exception) when (exception is SqlException { Number: 2601 or 2627 } || exception.InnerException is SqlException { Number: 2601 or 2627 })
                {
                    // 另一个进程刚创建了同节点水位，重新读取后进行 CAS。
                    continue;
                }
            }
            var previous = record.LastTimestamp;
            var next = Math.Max(currentMilliseconds, previous + 1);
            if (next > currentMilliseconds + 5000) { throw new InvalidOperationException("节点时间水位领先超过 5 秒，请检查节点配置与时钟。"); }
            if (database.Client.Updateable<SnowflakeNodeRecord>().SetColumns(item => item.LastTimestamp == next)
                .Where(item => item.WorkerId == workerId && item.LastTimestamp == previous).ExecuteCommand() == 1) { return next; }
        }
        throw new InvalidOperationException("节点时间片竞争过于频繁，请为实例分配独立节点号。");
    }
}

/// <summary>基础设施水位表，以节点号为技术主键；不是业务实体，也不使用生成中的雪花 ID。</summary>
[SugarTable("dbo.system_snowflake_nodes")]
public sealed class SnowflakeNodeRecord
{
    [SugarColumn(ColumnName = "worker_id", IsPrimaryKey = true)] public long WorkerId { get; set; }
    [SugarColumn(ColumnName = "last_timestamp")] public long LastTimestamp { get; set; }
}
