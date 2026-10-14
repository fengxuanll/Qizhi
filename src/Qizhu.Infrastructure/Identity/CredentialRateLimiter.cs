using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Qizhu.Application.Abstractions;
using Qizhu.Application.Common;
using Qizhu.Infrastructure.Database;
using SqlSugar;

namespace Qizhu.Infrastructure.Identity;

/// <summary>持久化登录限流接口，数据库计量避免多实例内存限流被绕过。</summary>
public interface ICredentialRateLimiter
{
    Task RequirePermitAsync(string userName, string address);
    Task RecordFailureAsync(long userId);
}

public sealed class CredentialRateLimiter(IDatabaseConnection database, ILookupNormalizer normalizer, IIdGenerator ids, TimeProvider clock) : ICredentialRateLimiter
{
    public async Task RequirePermitAsync(string userName, string address)
    {
        await database.Client.Ado.BeginTranAsync(IsolationLevel.Serializable);
        try
        {
            var accountCount = await IncrementAsync("account", normalizer.NormalizeName(userName) ?? string.Empty);
            var ipCount = await IncrementAsync("ip", address);
            await database.Client.Ado.CommitTranAsync();
            // 拒绝的请求也提交计数，恶意请求不能通过回滚恢复额度。
            if (accountCount > 5 || ipCount > 20) { throw new ApiException(429, "rate_limit.exceeded", "登录尝试过于频繁，请稍后重试。"); }
        }
        catch
        {
            database.Client.Ado.CancellationToken = null;
            if (database.Client.Ado.Transaction is not null) { await database.Client.Ado.RollbackTranAsync(); }
            throw;
        }
    }

    /// <summary>更新表达式在数据库内原子增加失败计数，并同步改变并发戳，避免旧对象覆盖新计数。</summary>
    public Task RecordFailureAsync(long userId)
    {
        var now = clock.GetUtcNow();
        var until = now.AddMinutes(15);
        var stamp = Guid.NewGuid().ToString("N");
        return database.Client.Updateable<FrameworkUser>().SetColumns(user => new FrameworkUser
        {
            AccessFailedCount = user.AccessFailedCount + 1 >= 5 ? 0 : user.AccessFailedCount + 1,
            LockoutEnd = user.AccessFailedCount + 1 >= 5 ? until : user.LockoutEnd,
            ConcurrencyStamp = stamp,
            UpdatedAt = now
        }).Where(user => user.Id == userId && (user.LockoutEnd == null || user.LockoutEnd <= now)).ExecuteCommandAsync();
    }

    private async Task<int> IncrementAsync(string kind, string key)
    {
        var now = clock.GetUtcNow();
        var window = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        // Serializable + UpdLock 保证缺失行的范围锁和已有行的更新锁，避免读取后写入竞争。
        var record = await database.Client.Queryable<LoginWindowRecord>().With(SqlWith.UpdLock)
            .Where(item => item.Kind == kind && item.KeyHash == hash && item.WindowStart == window).SingleAsync();
        if (record is null)
        {
            record = new LoginWindowRecord { Id = ids.NextId(), Kind = kind, KeyHash = hash, WindowStart = window, RequestCount = 1 };
            await database.Client.Insertable(record).ExecuteCommandAsync();
        }
        else
        {
            await database.Client.Updateable<LoginWindowRecord>().SetColumns(item => item.RequestCount == item.RequestCount + 1)
                .Where(item => item.Id == record.Id).ExecuteCommandAsync();
            record.RequestCount++;
        }
        return record.RequestCount;
    }
}
