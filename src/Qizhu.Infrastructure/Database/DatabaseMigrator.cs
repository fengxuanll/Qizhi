using DbUp;

namespace Qizhu.Infrastructure.Database;

public static class DatabaseMigrator
{
    /// <summary>仅由显式初始化或发布命令调用，不在 API 正常启动时自动修改结构。</summary>
    public static void Upgrade(string connectionString)
    {
        var result = DeployChanges.To.SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DatabaseMigrator).Assembly)
            .JournalToSqlTable("dbo", "qizhu_schema_versions")
            .WithTransaction()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
        {
            // 原始异常可能包含连接信息，只向命令调用方暴露可操作的概述。
            throw new InvalidOperationException("数据库初始化失败，请检查数据库连接、权限和现有结构。", result.Error);
        }
    }
}
