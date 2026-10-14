using Qizhu.Application.Abstractions;
using Qizhu.Infrastructure.Database;

namespace Qizhu.Infrastructure.Auditing;

/// <summary>写入与业务同事务的审计事件，可替换为其他持久化实现。</summary>
public interface IAuditWriter
{
    Task WriteAsync(long tenantId, string action, long resourceId, string summary);
}

/// <summary>仅通过实体插入；共享当前数据库会话，审计失败时业务事务也回滚。</summary>
public sealed class AuditWriter(IDatabaseConnection connection, ICurrentActor actor, IIdGenerator ids, TimeProvider clock) : IAuditWriter
{
    public Task WriteAsync(long tenantId, string action, long resourceId, string summary) => connection.Client.Insertable(new AuditRecord
    {
        Id = ids.NextId(),
        TenantId = tenantId,
        ActorId = actor.UserId,
        Action = action,
        ResourceId = resourceId,
        Summary = summary,
        OccurredAt = clock.GetUtcNow(),
        RequestId = actor.RequestId,
        TraceId = actor.TraceId,
        ActorType = "user",
        ResourceType = "projects",
        Result = "success"
    }).ExecuteCommandAsync();
}
