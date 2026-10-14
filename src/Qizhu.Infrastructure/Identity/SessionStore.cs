using Qizhu.Application.Abstractions;
using Qizhu.Infrastructure.Database;
using SqlSugar;

namespace Qizhu.Infrastructure.Identity;

[SugarTable("dbo.identity_sessions")]
public sealed class SessionRecord
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(ColumnName = "user_id")] public long UserId { get; set; }
    [SugarColumn(ColumnName = "tenant_id", IsNullable = true)] public long? TenantId { get; set; }
    [SugarColumn(ColumnName = "membership_id", IsNullable = true)] public long? MembershipId { get; set; }
    [SugarColumn(ColumnName = "security_stamp")] public string SecurityStamp { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "datetimeoffset(7)")] public DateTimeOffset CreatedAt { get; set; }
    [SugarColumn(ColumnName = "last_seen_at", ColumnDataType = "datetimeoffset(7)")] public DateTimeOffset LastSeenAt { get; set; }
    [SugarColumn(ColumnName = "expires_at", ColumnDataType = "datetimeoffset(7)")] public DateTimeOffset ExpiresAt { get; set; }
    [SugarColumn(ColumnName = "is_revoked")] public bool IsRevoked { get; set; }
}

public sealed class SessionStore(IDatabaseConnection connection, IIdGenerator ids, TimeProvider clock) : ISessionStore
{
    public async Task<SessionRecord> CreateAsync(FrameworkUser user, long? tenantId = null, long? membershipId = null)
    {
        var now = clock.GetUtcNow();
        var session = new SessionRecord
        {
            Id = ids.NextId(),
            UserId = user.Id,
            TenantId = tenantId,
            MembershipId = membershipId,
            SecurityStamp = user.SecurityStamp!,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now.AddHours(8)
        };
        await connection.Client.Insertable(session).ExecuteCommandAsync();
        return session;
    }

    public async Task<SessionRecord?> ValidateAsync(long sessionId, long userId)
    {
        var session = await connection.Client.Queryable<SessionRecord>().Where(item => item.Id == sessionId && item.UserId == userId && !item.IsRevoked).SingleAsync();
        if (session is null || session.ExpiresAt <= clock.GetUtcNow() || session.LastSeenAt <= clock.GetUtcNow().AddMinutes(-30)) { return null; }
        var user = await connection.Client.Queryable<FrameworkUser>().Where(item => item.Id == userId && item.IsEnabled).SingleAsync();
        if (user is null || user.SecurityStamp != session.SecurityStamp) { return null; }
        await connection.Client.Updateable<SessionRecord>().SetColumns(item => item.LastSeenAt == clock.GetUtcNow()).Where(item => item.Id == sessionId && !item.IsRevoked).ExecuteCommandAsync();
        return session;
    }

    public Task RevokeAsync(long sessionId, long userId) => connection.Client.Updateable<SessionRecord>().SetColumns(item => item.IsRevoked == true)
        .Where(item => item.Id == sessionId && item.UserId == userId).ExecuteCommandAsync();
}

/// <summary>持久化会话边界；空闲 30 分钟、绝对 8 小时，安全戳改变后立即失效。</summary>
public interface ISessionStore
{
    Task<SessionRecord> CreateAsync(FrameworkUser user, long? tenantId = null, long? membershipId = null);
    Task<SessionRecord?> ValidateAsync(long sessionId, long userId);
    Task RevokeAsync(long sessionId, long userId);
}
