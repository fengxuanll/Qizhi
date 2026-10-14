using Microsoft.AspNetCore.Identity;
using Qizhu.Application.Abstractions;
using Qizhu.Infrastructure.Database;
using Qizhu.Infrastructure.Tenancy;

namespace Qizhu.Infrastructure.Identity;

/// <summary>显式初始化入口；只创建缺失记录，禁止启动时重置已有管理员密码。</summary>
public interface IDatabaseBootstrapper
{
    Task<long> InitializeAsync(string userName, string password, long tenantId);
}

public sealed class DatabaseBootstrapper(IDatabaseConnection database, UserManager<FrameworkUser> users, RoleManager<FrameworkRole> roles,
    IIdGenerator ids, TimeProvider clock) : IDatabaseBootstrapper
{
    public async Task<long> InitializeAsync(string userName, string password, long tenantId)
    {
        // 1 是 Memory 演示占位值；新数据库初始化自动生成真正雪花租户 ID。
        if (tenantId <= 1)
        {
            var existing = await database.Client.Queryable<TenantRecord>().Where(item => item.Slug == "main").SingleAsync();
            tenantId = existing?.Id ?? ids.NextId();
        }
        var user = await users.FindByNameAsync(userName);
        if (user is null)
        {
            user = new FrameworkUser { UserName = userName, DisplayName = "系统管理员", LockoutEnabled = true };
            Require(await users.CreateAsync(user, password));
        }
        if (!await roles.RoleExistsAsync("platform_admin")) { Require(await roles.CreateAsync(new FrameworkRole { Name = "platform_admin" })); }
        if (!await users.IsInRoleAsync(user, "platform_admin")) { Require(await users.AddToRoleAsync(user, "platform_admin")); }
        var now = clock.GetUtcNow();
        if (!await database.Client.Queryable<TenantRecord>().Where(item => item.Id == tenantId).AnyAsync())
        {
            if (tenantId <= 0) { throw new InvalidOperationException("初始化默认租户必须指定正数雪花 ID。"); }
            await database.Client.Insertable(new TenantRecord { Id = tenantId, Slug = "main", Name = "默认工作空间", State = "active", ExpiresAt = now.AddYears(1), CreatedAt = now }).ExecuteCommandAsync();
        }
        var member = await database.Client.Queryable<MembershipRecord>().Where(item => item.TenantId == tenantId && item.UserId == user.Id).SingleAsync();
        if (member is null)
        {
            member = new MembershipRecord { Id = ids.NextId(), TenantId = tenantId, UserId = user.Id, Status = "active", DisplayName = "管理员", CreatedAt = now };
            await database.Client.Insertable(member).ExecuteCommandAsync();
        }
        var role = await database.Client.Queryable<TenantRoleRecord>().Where(item => item.TenantId == tenantId && item.Code == "tenant_admin").SingleAsync();
        if (role is null)
        {
            role = new TenantRoleRecord { Id = ids.NextId(), TenantId = tenantId, Code = "tenant_admin", Name = "租户管理员" };
            await database.Client.Insertable(role).ExecuteCommandAsync();
        }
        if (!await database.Client.Queryable<MembershipRoleRecord>().Where(item => item.TenantId == tenantId && item.MembershipId == member.Id && item.RoleId == role.Id).AnyAsync())
        {
            await database.Client.Insertable(new MembershipRoleRecord { Id = ids.NextId(), TenantId = tenantId, MembershipId = member.Id, RoleId = role.Id }).ExecuteCommandAsync();
        }
        foreach (var (code, name) in new[] { ("projects.projects.read", "查看项目"), ("projects.projects.create", "新增项目"), ("projects.projects.update", "修改项目"),
            ("projects.projects.delete", "删除项目"), ("projects.projects.restore", "恢复项目"), ("system.audit.read", "查看审计"), ("system.modules.read", "查看模块") })
        {
            if (!await database.Client.Queryable<PermissionRecord>().Where(item => item.Code == code).AnyAsync())
            {
                await database.Client.Insertable(new PermissionRecord { Id = ids.NextId(), Code = code, Name = name, Scope = "Tenant" }).ExecuteCommandAsync();
            }
            if (!await database.Client.Queryable<RolePermissionRecord>().Where(item => item.TenantId == tenantId && item.RoleId == role.Id && item.PermissionCode == code).AnyAsync())
            {
                await database.Client.Insertable(new RolePermissionRecord { Id = ids.NextId(), TenantId = tenantId, RoleId = role.Id, PermissionCode = code }).ExecuteCommandAsync();
            }
        }
        return tenantId;
    }
    private static void Require(IdentityResult result)
    {
        if (!result.Succeeded) { throw new InvalidOperationException("初始身份创建失败，请检查账号和密码策略。"); }
    }
}
