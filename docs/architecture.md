# 架构与扩展

具体的数据、接口、Identity 与运行决策以[数据、接口、安全与运行规范](platform-standards.md)为实现基线；以下描述模块职责与接入边界。

## 一次业务请求如何流转

`HTTP 请求 → Controller → Application Service → Domain / Repository → Infrastructure`

Controller 负责请求与响应；Service 编排用例；Domain 保持业务规则；Infrastructure 处理存储与外部服务。
注册集中在 API 宿主。应用层通过 `ICurrentTenant` 获取租户，通过 `TimeProvider` 获取 UTC 时间，通过 `IIdGenerator` 获取雪花 long 主键，避免依赖 HTTP 和系统时钟。

## 新增业务模块

以 Customers 为例：先在 Domain 定义客户规则，再在 Application 定义客户 DTO、所需仓储接口和用例。
在 Infrastructure 实现仓储，最后在 API 的 `Modules/Customers` 添加 Controller 并注册依赖。
新增接口默认受认证策略保护；只对确实公开的接口显式使用 `AllowAnonymous`。
演示 ProjectsController 的匿名访问标记不应复制到正式模块。

规则靠近业务：名称是否合法写在实体或用例里；不要将全部逻辑写在 Controller，也不要为了层数创建仅转发调用的服务。
优先为实际变化点定义接口；数据库、主键、租户、会话、审计已采用接口注入。公共持久化字段复用 EntityRecord / AuditedTenantRecord 抽象基类。跨模块通过公开用例交互，不直接访问其他模块的存储内部实现。

## 数据库接入

数据库与 ORM 已接入 SQL Server 2019 和 SqlSugar。所有活动表统一 dbo，业务主键为雪花 long。[全栈技术选型](technology-stack.md) 使用 SqlSugarCore 管理数据访问，并使用版本化 SQL 脚本管理生产数据库变更。
现有 SqlSugarProjectRepository 实现 `IProjectRepository`，TransactionalProjectWorkspace 实现 `IProjectWorkspace`，应用层调用无需变化；客户指定其他数据库时单独验证提供程序与迁移兼容性。
数据库实现必须在列表和详情查询同时约束 TenantId，增加对应索引；数据库级隔离或全局过滤可作为第二道防线。
SqlSugar 实现和持久化模型放在 Infrastructure，Domain 与 Application 不依赖 ORM 类型或映射特性。
查询过滤器不能替代更新、删除和原生 SQL 的租户条件；数据库客户端按请求作用域管理，避免共享可变的租户过滤状态。
新增业务还需明确事务边界、并发策略、唯一约束和迁移流程。软删除与审计字段按实际业务添加。
业务主实体默认采用规范定义的审计与软删除字段；DateTimeOffset 映射 datetimeoffset(7)，可编辑实体使用 rowversion，并通过 If-Match 校验并发。

SqlServer 模式使用真实持久化；Memory 仅用于匿名演示和单元测试。实体写入使用 SqlSugar 表达式，DDL/数据迁移保留版本化 SQL。共享 IDatabaseConnection 保证项目、审计和幂等处于同一事务；雪花水位预留使用独立连接，业务回滚不能复用 ID。

## 正式身份与多租户

SingleTenant 始终使用配置的 DefaultTenantId。MultiTenant 从认证身份的 `tenant_id` Claim 获取租户。
匿名开发示例可通过 X-Tenant-Id 模拟租户，但不构成认证或授权。
正式登录或租户切换流程必须校验成员资格、租户状态和权限，再签发身份；不得直接把客户端选择写入 Claim。
Identity 采用全局用户与平台角色；租户成员、租户角色及权限另行管理。每请求确认会话和租户状态，后台任务持久化租户上下文。
多租户未来还要覆盖缓存键、文件路径、后台任务、导出文件和审计数据。

已实现全局账号、平台角色、租户成员/角色/权限码以及接口权限过滤器。前端菜单和按钮仅控制展示；每请求和写事务均重新校验成员及权限。
Cookie 管理端已接入 HTTPS、CSRF、持久化会话、安全戳失效、退出撤销、TOTP 与恢复码；生产必须配置共享受保护密钥环和证书。

## 运维边界

`/health/live` 只检查进程存活。SQL Server 模式已增加 /health/ready 数据库就绪检查，避免将存活与依赖状态混在一起。
日志不记录请求体、查询字符串和凭证；未知异常只向客户端返回通用信息与追踪编号。
正式部署需配置可信反向代理、域名 AllowedHosts、HTTPS 和认证密钥持久化；敏感配置通过环境变量或密钥服务注入。
示例容器默认 Production，业务演示和文档不会开放。Docker 构建验证需要本机 Docker 环境。
