# 实现与迁移记录

更新：2026-10-15。此文记录已落地能力；总体建设目标见 platform-standards.md。

## 本次六项约束

| 要求 | 实现位置与行为 |
| --- | --- |
| 雪花长整型主键 | IIdGenerator / SnowflakeIdGenerator；业务与关联表 bigint，IdentityUser<long> / IdentityRole<long>；数据库持久化节点水位防重启重复 |
| 实体映射操作数据库 | 项目、Identity、权限关联、会话、登录计量、审计、幂等使用 SqlSugar 实体查询/表达式更新，业务代码无手写 SQL |
| 接口与抽象类 | IProjectRepository / IProjectWorkspace / IDatabaseConnection / ISnowflakeStateStore / ITenantDirectory / ISessionStore / ICredentialRateLimiter / IAuditWriter / IDatabaseBootstrapper；EntityRecord / AuditedTenantRecord |
| 注释清楚 | 公共契约、实体基类、雪花布局/水位/回退、事务重新鉴权、同事务审计、登录原子计量、安全与 UTC 均有中文说明 |
| UTC 保存、访问时区显示 | datetimeoffset(7) + 偏移零 CHECK；领域日期归一化，时间来自 TimeProvider；前端 datetime.ts + Intl 浏览器默认时区 |
| 所有表 dbo | 新增 0003 迁移并更新 SugarTable 映射；使用 identity_/tenancy_/system_/audit_ 前缀，业务示例 dbo.projects |

## 主数据库迁移

连接使用本机 SQL Server 2019 命名实例，数据库 Qizhu；凭证仅保存在忽略的 appsettings.Local.json，未进入本文或模板。实例原先指定的 1433 未监听，现使用命名实例连接。

迁移前使用 COPY_ONLY/CHECKSUM 完整备份并 VERIFYONLY；备份位于忽略的 work/backups/Qizhu-before-snowflake.bak。完整迁移在事务内预验证并回滚，随后由 DbUp 执行正式 0003。旧数据逐表存入 dbo.system_legacy_archive，并保留 dbo.system_legacy_ids 旧 GUID→雪花映射。迁移在同一事务内校验 JSON 归档及业务复制行数，外键保证关系一致，成功后清理旧模块 schema 下的表。

账号及密码哈希、平台/租户角色、成员关系、权限、项目、软删除和审计保留。旧会话统一撤销，旧幂等记录归档并重新开始有效窗口，rowversion 重新生成；旧客户端需要重新登录、刷新 URL 与 ETag。本地 DefaultTenantId 已更新为映射后的雪花 ID。0001/0002 保持历史内容，新库初始化按版本执行到 0003。

## 验证与边界

真实数据库 API 已验证登录、租户切换、中文/表情往返、项目写入、相同幂等键重放、更新、旧 ETag 返回 412、软删除及详情不可见。构建与单元测试覆盖雪花位布局、进程重新创建/并发唯一性、时钟回退、长整数 JSON 精度、非零偏移归一化和前端跨时区日期展示。完整检查运行 scripts/check.ps1。

当前前端页面为登录、概览、租户切换、项目 CRUD/回收站、密码修改与 MFA 设置。配额/缓存/模块开关、用户与组织管理、完整生命周期、文件、导入导出、审批、消息、任务与生成器尚待建设；模块目录接口只列出当前模块。备份校验不等于完整恢复演练，容量与 RPO/RTO 仍需后续验证。生产密钥环、加密证书、HTTPS 与可信代理由部署配置提供。
