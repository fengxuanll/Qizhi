using Microsoft.AspNetCore.Identity;
using Qizhu.Application.Abstractions;
using Qizhu.Infrastructure.Database;

namespace Qizhu.Infrastructure.Identity;

public sealed class SqlSugarRoleStore(IDatabaseConnection connection, IIdGenerator ids) : IRoleStore<FrameworkRole>
{
    public void Dispose() { }
    public Task<string> GetRoleIdAsync(FrameworkRole role, CancellationToken cancellationToken) => Task.FromResult(role.Id.ToString());
    public Task<string?> GetRoleNameAsync(FrameworkRole role, CancellationToken cancellationToken) => Task.FromResult(role.Name);
    public Task SetRoleNameAsync(FrameworkRole role, string? name, CancellationToken cancellationToken) { role.Name = name; return Task.CompletedTask; }
    public Task<string?> GetNormalizedRoleNameAsync(FrameworkRole role, CancellationToken cancellationToken) => Task.FromResult(role.NormalizedName);
    public Task SetNormalizedRoleNameAsync(FrameworkRole role, string? name, CancellationToken cancellationToken) { role.NormalizedName = name; return Task.CompletedTask; }
    public async Task<FrameworkRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken) =>
        await connection.Client.Queryable<FrameworkRole>().Where(role => role.Id == long.Parse(roleId)).SingleAsync();
    public async Task<FrameworkRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken) =>
        await connection.Client.Queryable<FrameworkRole>().Where(role => role.NormalizedName == normalizedRoleName).SingleAsync();
    public async Task<IdentityResult> CreateAsync(FrameworkRole role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (role.Id == 0) { role.Id = ids.NextId(); }
        await connection.Client.Insertable(role).ExecuteCommandAsync();
        return IdentityResult.Success;
    }
    public async Task<IdentityResult> UpdateAsync(FrameworkRole role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previousStamp = role.ConcurrencyStamp;
        role.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        var count = await connection.Client.Updateable(role).Where(item => item.Id == role.Id && item.ConcurrencyStamp == previousStamp).ExecuteCommandAsync();
        return count == 1 ? IdentityResult.Success : IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
    }
    public async Task<IdentityResult> DeleteAsync(FrameworkRole role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = await connection.Client.Deleteable<FrameworkRole>().Where(item => item.Id == role.Id && item.ConcurrencyStamp == role.ConcurrencyStamp).ExecuteCommandAsync();
        return count == 1 ? IdentityResult.Success : IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
    }
}
