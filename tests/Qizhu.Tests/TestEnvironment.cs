using System.Runtime.CompilerServices;

namespace Qizhu.Tests;

internal static class TestEnvironment
{
    // 测试宿主不能读取开发者的数据库密码或默认连接真实数据库。
    [ModuleInitializer]
    internal static void Initialize() => Environment.SetEnvironmentVariable("QIZHU_IGNORE_LOCAL_CONFIGURATION", "1");
}
