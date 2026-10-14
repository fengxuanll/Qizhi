# Qizhu 开发基础框架

C# / .NET 10 + SQL Server + SqlSugar 后端，Vue 3 / TypeScript / Element Plus 管理端。按模块组织，Domain → Application → Infrastructure → Api；新项目可通过本地模板复用。

## 本项目启动

需要 .NET 10 SDK、Node.js 24 和 SQL Server 2019 或更高版本。当前主数据库已接入，连接密码和初始管理员配置位于被忽略的 `src/Qizhu.Api/appsettings.Local.json`。

```powershell
# 首次准备依赖与本地 HTTPS 证书；如已准备过可以直接 dev
./scripts/setup.ps1 -TrustCertificate
./scripts/dev.ps1
```

管理端 `https://localhost:5173`，API `http://localhost:5080`。从管理端登录，账号与初始密码使用本地 `Bootstrap` 配置，系统不会重复初始化或重置已有密码。运行脚本也可在 scripts 目录直接执行 `./dev.ps1`，中文脚本使用 UTF-8 BOM 兼容 Windows PowerShell 5.1。

```powershell
./scripts/dev.ps1 -ApiOnly
./scripts/check.ps1
```

健康检查 `/health/live`、`/health/ready`；Development 开放 `/openapi/v1.json`。SqlServer 模式关闭匿名业务演示接口。

## 新项目初始化

```powershell
dotnet new install .
dotnet new qizhu-framework --name Acme --output ../Acme
```

模板替换工程名、命名空间与引用，包含前后端，排除密码、私钥、数据库备份、缓存和构建产物。在新项目复制 `appsettings.Local.example.json` 为 `appsettings.Local.json`，填写数据库与初始管理员配置，执行 `./scripts/setup.ps1 -InitializeDatabase -TrustCertificate`。

新数据库默认多租户模式。初始化自动生成默认租户雪花 ID 并输出该 ID；独立部署将其写入 `Framework:DefaultTenantId`，同时设置 `DeploymentMode=SingleTenant`。示例值 1 仅用于 Memory 模式。已存在数据库升级前先备份，迁移命令不会重置密码。

## 代码基线

- 所有业务主键、关联记录主键及外键为雪花 `long` / `bigint`，JSON ID 是十进制字符串。
- 业务数据操作使用 SqlSugar 实体和表达式；表统一 `dbo`，结构变更由版本化迁移管理。
- 通过 `IIdGenerator`、`IDatabaseConnection`、`ITenantDirectory`、`ISessionStore`、`IAuditWriter` 等接口扩展；公共字段复用抽象实体基类。
- 保存 UTC `DateTimeOffset`，数据库限制偏移为零；前端公共方法按访问人的时区显示。
- 已有登录、CSRF、TOTP/恢复码、持久化会话、租户权限、项目 CRUD/回收站、ETag 并发校验、幂等事务及审计。

## 工程目录与边界

| 目录 | 职责 |
| --- | --- |
| src/Qizhu.Domain | 领域规则，保持不依赖 ORM |
| src/Qizhu.Application | 用例、仓储接口与 DTO |
| src/Qizhu.Infrastructure | SqlSugar 映射、Identity、事务、迁移、雪花水位 |
| src/Qizhu.Api | HTTP、授权、配置、错误、日志、DI |
| web | 管理端、类型化请求、权限按钮与时区展示 |
| tests / scripts | 测试、环境准备、启动与检查 |
| docs | 技术选型、20 项平台规范与实施状态 |

当前是可运行的基础框架。组织/字典/参数/文件/导入导出/审批/通知/任务/代码生成、完整租户生命周期、订阅配额、缓存和模块开关仍待开发；RPO/RTO 和性能数字尚未经过完整生产验收。

文档：[技术栈](docs/technology-stack.md)、[平台规范](docs/platform-standards.md)、[架构](docs/architecture.md)、[开发约定](docs/development.md)、[实现与迁移记录](docs/implementation-status.md)。

后续工作与当前流程缺口见[未完成工作反查](docs/remaining-work.md)。
