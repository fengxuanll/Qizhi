# Qizhu 数据、接口、安全与运行规范

更新日期：2026-10-15。适用技术：.NET 10、SQL Server、SqlSugar、Vue 3 管理端。

本文是后续实现的默认规范，补充[全栈技术选型](technology-stack.md)。除明确说明的已有行为外，新增字段、模型、策略、配额和指标均**尚未实现或验收**。性能与恢复数字是目标，不代表当前系统已经达到。项目有不同业务要求时，在项目级决策记录中明确覆盖值、理由及验收结果。

## 1. 数据模型与主键策略

### 1.1 标识与存储

| 对象 | 默认策略 |
| --- | --- |
| 业务实体、用户、租户、成员、角色、关联、权限、审计、计量 | Id 为正数 C# long / SQL Server bigint，由 IIdGenerator 生成雪花 ID，不使用数据库自增 |
| 对外标识 | JSON 使用十进制字符串，如 "237000000000004096"；前端始终使用 string，不能通过 Number / parseInt 转换 |
| 物理组织 | bigint Id 为聚集主键，不再增加 StorageSequence；创建时间仍以 created_at 为准 |
| 关联表 | 同样有雪花 Id，原关系组合保留 UNIQUE，租户关联同时使用同租户复合外键 |
| 权限与模块定义 | 雪花 Id 作为主键，稳定 Code 建唯一约束；机器码采用限定长度 varchar 与二进制排序规则 |
| 单租户部署 | 使用已初始化的雪花租户 ID；0 不能表示“所有租户”，1 仅是 Memory 演示占位值 |
| 基础设施元数据 | DbUp 执行日志使用组件自身键；雪花水位以 bigint worker_id 为技术键，避免生成器递归调用自身；两者不属于业务主键 |

雪花布局为 41 位相对 UTC 纪元毫秒、10 位节点、12 位序列，纪元固定为 2025-01-01T00:00:00Z，每时间片最多 4096 个 ID。Snowflake:WorkerId 范围 0～1022，1023 专供历史迁移，不能分配给运行实例。新项目改变纪元必须视为不兼容协议变更。

正式 SQL Server 模式由 SqlSugarSnowflakeStateStore 原子预留毫秒时间片：读取数据库持久化水位后用 CAS 条件更新，预留使用独立连接，不能随业务事务回滚。即使同节点进程重启或误启动两个实例，也不会复用时间片；正常部署仍给每个实例分配不同节点号。数据库不可用、校时回退、水位领先超过 5 秒或时间位溢出时拒绝生成，不能退回随机数/自增。Memory 模式水位仅限演示与测试，不保证重启后唯一。

GUID→雪花迁移由新增 0003 执行，0001/0002 不修改。迁移前完整备份并校验；事务内为旧主键/引用建立 dbo.system_legacy_ids 映射、保存 dbo.system_legacy_archive 完整逐表 JSON 快照、复制记录并核对行数和外键。成功后旧表清理，活动表全部位于 dbo。旧 Cookie 撤销，旧幂等响应仅归档，客户端重新登录并刷新旧资源 URL/ETag。归档含身份历史数据，只供受控迁移维护使用，不开放普通 API。

### 1.2 关系与约束

- 以聚合为事务边界；金额、状态、余额等关键规则同时使用应用校验与数据库约束。
- 租户主实体增加唯一约束 (tenant_id, id)。租户内外键同时包含 tenant_id，防止把其他租户的资源挂到当前记录上。
- 租户业务唯一键默认包含 tenant_id，如 (tenant_id, normalized_code)；可复用的软删除编号采用 is_deleted=0 的过滤唯一索引。
- 默认不配置跨聚合级联删除。关系清理与状态变更通过业务用例执行；模块间共享稳定标识，不直接操作其他模块内部表。
- SqlSugar 映射、数据库记录类型及持久化属性位于 Infrastructure；Domain 与 Application 不依赖 SugarTable / SugarColumn。
- 数据库结构由版本化 SQL 脚本和 DbUp 管理；变更先验证旧数据、索引及回滚或前向修复路径，再随发布执行。

## 2. 类型映射、时间与 collation

### 2.1 默认映射

| C# / 业务类型 | SQL Server 类型 | API 表示与要求 |
| --- | --- | --- |
| long 主键及外键 | bigint | 十进制字符串；禁止前端数值化 |
| bool | bit | JSON boolean |
| int / short / byte | int / smallint / tinyint | JSON number；保持范围校验 |
| long | bigint | 对外字段使用十进制字符串，避免 JavaScript 精度丢失 |
| decimal 金额 | decimal(19,4) | 十进制字符串；同时提供 ISO 货币代码，按币种校验有效小数位 |
| decimal 数量 | decimal(18,6) | 十进制字符串；更高精度必须显式定义，不能静默舍入 |
| double / float | float(53) / real | 仅用于允许近似值的统计或测量，不用于金额 |
| string 普通文本 | nvarchar(n) | 必须定义长度；显示名默认 100、说明默认 1000；不同字段可明确覆盖 |
| string 机器代码 | varchar(n) | 限制 ASCII 和格式；权限码默认 128、模块码默认 64 |
| DateTimeOffset | datetimeoffset(7) | ISO 8601；表示时间点，写入前转 UTC，输出 Z 或 +00:00 |
| DateTime（外部输入兼容） | 不作为默认实体时间类型 | 必须明确来源时区并转换为 UTC DateTimeOffset；拒绝无时区时间点 |
| DateOnly | date | yyyy-MM-dd |
| TimeOnly | time(7) | HH:mm:ss，存在小数秒时按精度输出 |
| 时间长度 | bigint | 存储毫秒，字段名带 _milliseconds；不能把 TimeSpan 当一天内时刻 |
| enum | smallint | 显式固定数值，数据库 CHECK 限制；API 使用稳定的小写字符串代码 |
| 并发令牌 byte[] | rowversion | Base64URL 字符串，见并发规范 |
| JSON 元数据 | nvarchar(max) + ISJSON 约束 | 仅用于扩展元数据、模板定义；大小上限 64 KiB，按 UTF-8 序列化后的字节数验证 |
| 二进制文件内容 | 对象存储 | 数据库只存储引用、大小、摘要、状态等元数据 |

**明确决定：DateTimeOffset 映射 datetimeoffset(7)，不隐式转为 datetime2。** 业务与审计时间点归一为 UTC，源时区有业务意义时另外记录 IANA TimeZoneId，如 Asia/Shanghai；偏移值不等于完整时区规则。[datetimeoffset](https://learn.microsoft.com/en-us/sql/t-sql/data-types/datetimeoffset-transact-sql?view=sql-server-ver16)、[datetime2](https://learn.microsoft.com/en-us/sql/t-sql/data-types/datetime2-transact-sql?view=sql-server-ver16)。

SqlSugar 的列类型必须显式映射并做往返测试：中文、表情、闰日、非零偏移转 UTC、7 位小数秒、decimal 边界及 NULL。不能依赖 CodeFirst 推断来决定正式数据库类型。

当前业务、会话、登录窗口及幂等过期统一由注入的 TimeProvider.GetUtcNow() 生成；外部 DateTimeOffset 使用 ToUniversalTime() 归一化。数据库时间列添加 DATEPART(TZOFFSET, 列)=0 的 CHECK，禁止任何写入保留非零偏移。生产节点校时，时钟偏差目标不超过 1 秒；后续跨节点配额与租约应通过独立权威时钟接口实现，不得在业务代码随意混用本地时间。前端统一使用 web/src/utils/datetime.ts，根据访问人的浏览器时区格式化；如用户选择展示时区，显式传入 IANA 时区。日期与每日钟点使用 DateOnly/TimeOnly，并单独保存计划所属 IANA 时区，不能伪装成 UTC 时间点。

### 2.2 字符串与排序规则

- 默认数据库 collation：Latin1_General_100_CI_AS_SC。普通文本统一 nvarchar，业务查询默认忽略大小写、区分重音，支持补充字符。
- 权限码、模块码、规范化登录名、规范化业务编号等机器字段显式使用 Latin1_General_100_BIN2；输入先按规范归一化，再做唯一性与精确匹配。
- 普通文本输入采用 Unicode NFC；用户名规范化采用 NFKC、Trim、ToUpperInvariant，保留原始显示名。密码不 Trim、不改变大小写或做 Unicode 归一化。
- 初期登录用户名限定 3～64 位 ASCII 字母、数字及 . _ @ -，首字符为字母或数字；显示姓名允许 Unicode。NormalizedUserName 使用 nvarchar(128) 并建立全局唯一索引。
- 不在 SQL 查询中拼接用户提供的 collation、字段名或排序方向。跨库临时表和联表查询的 collation 必须保持兼容。
- Code、名称等必填文本 Trim 后不能为空；可选文本为空白时统一转 NULL。字段长度验证采用与存储一致的约束，另外按字节限制大型输入。

排序规则会影响比较与索引行为；修改数据库或列的 collation 必须单独验证唯一键和查询计划。[SQL Server collation 与 Unicode](https://learn.microsoft.com/en-us/sql/relational-databases/collations/collation-and-unicode-support?view=sql-server-ver16)。

## 3. 表、列、索引与模型命名

| 范围 | 规范与示例 |
| --- | --- |
| SQL schema | 所有数据表统一 dbo；不按业务模块创建 schema |
| 表 | 小写 snake_case，以模块前缀区分，如 dbo.identity_users、dbo.tenancy_tenant_memberships、dbo.audit_events；示例业务表 dbo.projects |
| 列 | 小写 snake_case；如 tenant_id、created_at、row_version |
| C# | 类、属性 PascalCase，局部变量 camelCase；数据库映射明确指定，不依赖隐式猜测 |
| 持久化类型 | ProjectRecord 等名称，区别于领域 Project；映射与查询集中于 Infrastructure |
| API JSON | camelCase，沿用现有接口风格 |
| 主键 / 外键 / 索引 | pk_{schema}_{table}、fk_{schema}_{table}_{target}、ix_{schema}_{table}_{columns} |
| 唯一 / 聚集 / CHECK | uq_{schema}_{table}_{columns}、cx_{schema}_{table}_{columns}、ck_{schema}_{table}_{rule} |

对象名最长 128 字符；长索引名截断前缀并追加稳定的 8 位摘要，生成器输出必须确定。索引按真实查询定义，租户查询优先 tenant_id；列表建议 (tenant_id, created_at DESC, id) 并覆盖必要列。

## 4. 软删除与审计字段

租户内可编辑的业务主实体默认具有以下持久化字段；全局用户等全局实体省略 tenant_id，不允许为其伪造“全局租户”：

| 字段 | 类型 / 初值 | 规则 |
| --- | --- | --- |
| id / tenant_id | bigint | 由服务端确定，创建后不可随普通编辑改变 |
| created_at / updated_at | datetimeoffset(7) | 创建时设置为同一 UTC 时间；变更时仅更新 updated_at |
| created_by / updated_by | bigint NULL | 指向全局用户；系统任务可以为空，其服务身份写入审计事件 |
| is_deleted | bit，默认 0 | 默认查询排除删除记录 |
| deleted_at / deleted_by | datetimeoffset(7) / bigint NULL | 删除时设置；恢复时清空，同时更新审计字段与并发版本 |
| row_version | rowversion | 数据库生成，应用不可赋新值 |

- 原生 SQL、更新、删除和导出也必须应用 tenant_id 与 is_deleted 条件；维护和回收站访问需要专门权限，不能通用地关闭过滤器。
- 删除与恢复均进行并发校验。恢复前检查当前租户的业务唯一键与父实体状态，冲突返回 409。
- 默认软删除保留 30 天，再由受控清理任务物理删除；业务已结算数据、审批历史、审计事件和留存冻结数据不适用自动清理。
- 模块定义、关联表、幂等记录、计量记录和追加式日志按自身生命周期处理，不强制继承全部审计与软删除字段。
- 审计事件至少包含雪花 id、occurredAt、tenantId、actorType、actorId、action、resourceType、resourceId、result、requestId、traceId 与脱敏后的变更摘要。
- 关键业务变更与数据库审计事件在同一事务提交；外部审计投递使用持久化 Outbox。业务审计默认保留 365 天，与普通运行日志分开管理。
- 密码、令牌、密钥、完整身份证件和文件正文不进入审计。用户注销保留稳定标识关联，按数据留存流程清理或匿名化个人信息。

## 5. 并发与事务控制

**默认采用 SQL Server rowversion 乐观并发控制。** rowversion 是数据库生成的版本标识，不是时间戳或更新时间。[SQL Server rowversion](https://learn.microsoft.com/en-us/sql/t-sql/data-types/rowversion-transact-sql?view=sql-server-ver16)。

- 可编辑资源的详情与修改结果返回 version，并附带强 ETag；version 为 8 字节 rowversion 的 Base64URL，ETag 是带双引号的同一值。
- PUT、DELETE、恢复和关键状态变更要求单个强 If-Match；缺失返回 428，格式不正确返回 400，版本过期返回 412，不接受 * 或弱 ETag 绕过检查。
- 更新条件至少为 id、tenant_id、is_deleted=0、row_version=@expected，更新列使用白名单，禁止将请求 DTO 整体覆盖实体。
- 受影响行数为零时，在当前租户范围核对：资源不存在或不可见返回 404；资源存在但版本改变返回 412。检查不查询其他租户。
- SqlSugar 将 row_version 映射为数据库生成、插入与更新忽略的列；使用显式条件更新及数据库输出或同事务读取取得新版本。具体 API 以锁定版本验证，不仅依靠 ORM 特性名称。
- 审批、余额、配额与编号分配还需数据库唯一约束或条件更新；涉及竞争的计量行使用局部锁和短事务，不默认开启全库串行化。
- 租户业务写事务内重新核验账号、成员、租户状态和授权版本，保留必要的状态读锁至提交；停用与权限变更等待已经进入提交事务的操作完成，变更提交后新的写入被拒绝。默认锁顺序为幂等记录 → 账号 / 会话 → 租户 / 成员 → 配额 → 业务记录，批量记录按稳定 ID 排序。
- 事务内避免远程 HTTP 调用，默认 5 秒目标、最长 30 秒执行上限；数据库命令默认 15 秒，超时回滚。批量后台任务分批处理，单批初始上限 500 行。
- 不默认使用 NOLOCK。死锁只在操作幂等且无外部副作用时最多重试 2 次，采用带抖动退避，且仍服从请求超时。

## 6. API 契约

| 项目 | 默认契约 |
| --- | --- |
| 路由与版本 | /api/v1/{resource}；平台管理使用 /api/v1/platform/*，租户业务由可信上下文确定 |
| JSON | UTF-8、camelCase；成功直接返回 DTO，错误使用 application/problem+json，不包裹统一 HTTP 200 |
| 创建 / 查询 / 更新 / 删除 | 创建 201 + Location；查询与更新 200；删除成功 204；异步受理 202 + 可查询的任务资源 |
| 分页 | pageNumber 从 1 开始、pageSize 默认 20、范围 1～100；返回 items、totalCount、pageNumber、pageSize |
| 排序 | 仅允许模块白名单字段；默认 createdAt DESC、id 作稳定的第二排序键 |
| 排序参数 | sortBy 为一个允许字段、sortDirection 为 asc / desc；非法字段或方向返回 request.invalid，初期不接受任意表达式排序 |
| 常用筛选 | keyword 最多 100 字符；createdFrom 包含、createdTo 不包含，均为带偏移的 ISO 8601 时间点；其他筛选按资源明确定义 |
| 筛选与字段选择 | 初期不提供通用动态表达式或客户端自选字段，返回固定 DTO；未知筛选参数返回 request.invalid，关键字按字面搜索并处理 LIKE 通配符转义 |
| 深分页 | 数据库阶段默认偏移上限 10000，超出返回 pagination.offset_exceeded；大结果采用专门的游标接口或后台导出 |
| PATCH | 初期不提供通用 JSON Patch；特殊业务通过专门用例及明确 DTO 表达 |
| 输入边界 | 普通 JSON 请求正文最多 1 MiB；超限 413。批量业务每次最多 100 项，默认原子提交 |
| DTO 保护 | 不接收 tenantId、审计字段、storageSequence、密码哈希或权限集合作为普通实体编辑字段 |
| 租户上下文校验 | 正式租户 API 要求 X-Tenant-Context 为页面当前租户的雪花 ID 十进制字符串，必须等于已验证身份的 tenant_id；它仅用于防止旧页面误写新租户，不作为身份来源 |
| 时间与精度 | 时间点统一 UTC ISO 8601；long 与 decimal 按类型规范传为字符串 |
| 兼容变更 | 新增可选字段可留在 v1；删除、改名、改类型或改变状态语义需新版本及迁移说明 |

接口说明必须在 OpenAPI 中声明错误、权限、限流、并发、幂等及租户条件。已发布版本原则上提供至少 90 天迁移窗口；紧急安全修复另行发布影响说明。

当前示例 DTO 和分页外形保留；并发令牌、幂等、深分页限制及完整错误契约属于后续新增能力。

## 7. 幂等键与重复请求

### 7.1 使用范围

- 业务创建、审批提交、任务提交及其他有副作用的 POST 要求 Idempotency-Key。PUT / DELETE 的关键状态变更同样启用；普通读取不要求。
- 登录、退出、验证码、密码重置及秘密签发接口不缓存响应，使用专门的一次性令牌、限流和状态机制。
- 键为 16～128 位 ASCII，允许字母、数字、. _ : -；客户端可使用 UUID。缺失返回 400 idempotency.key_required。
- 唯一范围为 scopeKind、scopeId、actorId、operationCode、keyHash。租户操作 scopeId=tenantId；平台操作使用独立的固定平台 scopeId，不能用空租户冒充平台。
- keyHash 使用 SHA-256；请求指纹覆盖方法、版本化操作标识、规范化查询参数、规范化 JSON 与 If-Match。JSON 对象键排序，数组顺序保留，指纹不含 Cookie、追踪头或原始幂等键。

### 7.2 执行与保存

- SQL Server 持久化幂等记录，建立唯一约束。业务写入、配额变化、审计、幂等结果及 Outbox 在同一事务中提交。
- 相同键与指纹重试返回原状态、响应正文、Location 和 ETag；回放前重新校验账号、成员、租户、模块及权限。
- 同键不同指纹返回 409 idempotency.key_conflict。同键正在执行时最多等待 5 秒，然后返回 409 idempotency.in_progress 与 Retry-After: 2。
- 结果默认保留 24 小时；超过保留期视为新请求，业务唯一约束继续防止持久重复。需要更长保护的业务在契约中延长时间并设计领域唯一键。
- 成功或确定的业务 4xx 结果可保存；未提交的事务、未知 5xx 或取消的请求不保存成功结果，重试依据事务状态执行。
- 保存响应上限 1 MiB；大型导出仅保存任务资源响应，不保存文件正文。文件与外部发送采用任务 / Outbox，不能把数据库幂等视为跨服务“恰好一次”。
- 幂等记录含请求指纹、结果引用或脱敏结果、创建及到期时间，不包含凭证。并发、崩溃、提交后断连和过期重试必须有专项测试。

## 8. 错误码字典

错误码采用小写点分形式，固定含义，禁止依赖中文提示判断逻辑。以下为基础字典；模块新增错误码时必须补充文档、HTTP 状态与处理建议。

| 错误码 | HTTP | 含义 / 客户端处理 |
| --- | --- | --- |
| request.invalid | 400 | JSON、参数、编码或字段校验失败；查看 errors |
| request.body_too_large | 413 | 输入超过接口限制 |
| pagination.invalid | 400 | 页码或页大小不合法；已有示例错误码 |
| pagination.offset_exceeded | 400 | 偏移超限，改用筛选、游标或导出 |
| auth.unauthenticated | 401 | 无有效会话，登录后重试 |
| auth.invalid_credentials | 401 | 登录失败，统一提示，不透露账号是否存在 |
| auth.session_expired | 401 | 会话过期、撤销或安全状态改变 |
| auth.forbidden | 403 | 缺少操作权限或数据范围 |
| tenant.required / tenant.invalid | 400 | 缺少或格式错误的租户标识；已有开发示例码 |
| tenant.membership_required | 403 | 当前身份不是可用租户成员 |
| tenant.context_required | 400 | 页面未提交预期租户上下文 |
| tenant.context_mismatch | 409 | 页面租户与当前会话不一致，重新加载页面 |
| tenant.unavailable | 403 | 租户未开通、已暂停或进入删除流程 |
| tenant.read_only | 403 | 租户只读，不能进行业务变更 |
| module.disabled | 403 | 模块未获得有效启用条件 |
| resource.not_found | 404 | 当前访问范围内资源不存在；不透露其他租户数据 |
| resource.duplicate | 409 | 当前范围内业务唯一键重复 |
| business.state_conflict | 409 | 当前状态不允许操作 |
| concurrency.precondition_required | 428 | 缺少 If-Match |
| concurrency.invalid_version | 400 | 并发令牌格式不正确 |
| concurrency.stale | 412 | 版本已改变，重新读取并提示用户处理 |
| idempotency.key_required / idempotency.key_invalid | 400 | 幂等键缺失或格式错误 |
| idempotency.key_conflict | 409 | 同键用于不同请求；新业务操作使用新键 |
| idempotency.in_progress | 409 | 原请求执行中，按 Retry-After 重试同键 |
| quota.exceeded | 409 | 容量或套餐配额已满；查看 resource、limit、used |
| rate_limit.exceeded | 429 | 窗口限流，按 Retry-After 重试 |
| upload.type_not_allowed | 400 | 文件类型或内容校验不通过 |
| upload.too_large | 413 | 文件或批次超过限制 |
| upload.rejected | 400 | 文件安全检查拒绝，不能发布 |
| dependency.unavailable | 503 | 必要外部依赖不可用 |
| server.error | 500 | 未知异常，仅显示通用提示及追踪编号 |
| project.name_required / project.name_too_long | 400 | 已有项目示例名称校验 |

基础格式为 type、title、status、detail、instance，并扩展 code、traceId、requestId；字段校验增加 errors。instance 使用不含敏感查询的请求路径。非敏感可重试信息使用 retryAfterSeconds 或响应头，绝不输出堆栈、SQL、连接字符串及内部文件路径。

```json
{
  "type": "about:blank",
  "title": "记录已被修改",
  "status": 412,
  "detail": "请刷新记录后重新确认修改。",
  "instance": "/api/v1/projects/11111111-1111-1111-1111-111111111111",
  "code": "concurrency.stale",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "requestId": "request-identifier"
}
```

现有异常处理器将业务规则统一返回 400，部分模型校验响应也还没有 code；接入完整字典时，需要明确错误类型到状态的映射并补全 401 / 403 / 404 等响应，不能只调整文档。

## 9. 权限码与数据范围

- 权限码格式为 module.resource.action，全小写 ASCII，最多 128 位。每段匹配 [a-z][a-z0-9_]*，恰好三段。
- 标准动作包括 read、create、update、delete、restore、import、export、approve、assign、manage。示例：projects.projects.read、system.dictionaries.manage、dbo.tenancy_tenants.manage。
- 权限注册在模块 manifest 的静态定义中，数据库记录定义与角色授权；显示名称可以变化，Code 发布后不复用或静默改义。
- 菜单、按钮、路由引用权限码；后端 Policy / Handler 做最终检查。角色名称不等于权限码，不持久化 * 或模糊通配权限。
- 平台权限与租户权限明确标记 Scope；租户角色不能授予平台权限。平台账号不自动绕过业务租户检查。
- 租户数据范围枚举为 tenant、department、department_descendants、own、selected_departments，选定部门必须属于当前租户。
- 多角色的同一操作数据范围取并集，结果仍与当前 tenantId 取交集；无授权为拒绝。初期不提供角色级显式 deny，以免形成不清晰的优先级。
- 查询、详情、导出和写入使用同一数据范围。改变权限或角色授权同时更新 TenantAuthorizationVersion，缓存和请求检查立即使用新版本。

## 10. 多租户 Identity 模型决策

**采用全局账号 + 租户成员关系 + 租户角色模型。** 同一人可加入多个租户；切换租户改变业务身份，不新建一套密码。

| 模型 / 表 | 关键字段及约束 |
| --- | --- |
| dbo.identity_users | 全局 Id、UserName、NormalizedUserName、PasswordHash、SecurityStamp、状态及锁定信息；NormalizedUserName 全局唯一，无 TenantId |
| dbo.identity_platform_roles / platform_user_roles | 平台级角色与关联；只能授予平台权限 |
| dbo.tenancy_tenants | 租户 Id、Slug、状态、有效期、状态版本、授权版本及当前订阅引用；Slug 全局唯一 |
| dbo.tenancy_tenant_memberships | Id、TenantId、UserId、状态、租户内显示资料与部门；(TenantId, UserId) 唯一 |
| dbo.tenancy_tenant_roles | Id、TenantId、Code、Name；(TenantId, Code) 唯一 |
| dbo.tenancy_membership_roles | 雪花 Id 主键，(TenantId, MembershipId, RoleId) 唯一约束，使用同租户外键 |
| dbo.tenancy_role_permissions | TenantId、RoleId、PermissionCode；唯一组合，并校验权限 Scope=Tenant |
| dbo.identity_sessions | SessionId、UserId、TenantId 可空、MembershipId 可空、安全版本、状态及过期时间 |

### 10.1 Identity 存储边界

- Identity 的 UserStore 使用 SqlSugar 管理全局账号、密码、锁定、声明与安全令牌；RoleStore / IUserRoleStore 只管理平台角色。
- 租户角色由独立的成员权限服务管理，不能用全局 UserManager.AddToRoleAsync 或 User.IsInRole 判断租户业务权限。
- 用户和角色模型分别继承 IdentityUser<long> / IdentityRole<long>，使用雪花键；Identity 持久化记录和 SecurityStamp 并发更新需在自定义存储中显式实现，不依赖默认 ORM 存储。
- 邮箱初期不作为租户内独立登录账号。若作为找回依据，已验证的规范化邮箱全局唯一；租户管理员只能发邀请或管理成员，不能修改全局密码、锁定或其他租户资料。
- 无租户上下文的全局身份访问只能由专门的身份 / 平台用例执行，不能通过临时关闭租户过滤获得。

ASP.NET Core Identity 支持自定义存储接口；UserStore、RoleStore 及实际需要的能力要配套实现和测试。[Identity 自定义存储](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-custom-storage-providers?view=aspnetcore-10.0)。

### 10.2 登录与切换租户

- 登录先验证全局账号，返回其可进入的成员租户；进入租户时重新校验成员、租户状态、模块及订阅，签发对应会话。
- 租户会话包含 ClaimTypes.NameIdentifier、session_id、tenant_id、membership_id 和必要安全版本；客户端选择只能作为切换请求，不能直接作为授权 Claim。
- 单个管理端 Cookie 当前只绑定一个租户。切换会更新 Cookie 并撤销旧租户会话；其他标签页收到通知后重新加载。正式租户 API 将 X-Tenant-Context 与已验证身份的 tenant_id 比较，缺失返回 400，不一致返回 409，防止旧标签页的创建请求意外落入新租户。
- 每次请求从主数据库确认会话、账号、成员及租户状态；权限版本也从可信数据读取。数据库不可用时受保护请求返回 503，不使用过期授权兜底。
- 后台任务持久化 TenantId、ActorId、所需权限和业务上下文，执行前复核，不复用已过期的浏览器 Cookie。
- 单租户部署同样检查当前账号是否为配置租户的可用成员；固定 DefaultTenantId 只确定数据范围，不替代成员授权。正式租户必须先完成初始化，不能把Memory 示例默认 ID 当作已存在的有效租户。

## 11. 租户生命周期

| 状态 | 进入条件 | 允许行为与退出方式 |
| --- | --- | --- |
| provisioning | 创建租户、分配订阅与初始化模块 | 只允许受控初始化；全部步骤完成后进入 trial 或 active；失败保留状态与可重试步骤 |
| trial | 初期默认试用 14 天 | 在试用套餐内正常操作；确认订阅后 active，到期后 read_only |
| active | 已开通且在有效期内 | 按订阅、模块、权限、配额操作；有效期结束转 read_only |
| read_only | 到期或主动停写 | 可读取与导出已授权数据，允许退出、改密和续订；拒绝业务变更；续订并校验后 active |
| suspended | 平台人工停用或安全处置 | 拒绝租户业务；平台可审计和恢复，保留停用原因；恢复到有效期决定的状态 |
| deletion_pending | 平台授权删除并二次确认 | 立即撤销租户会话、停止业务任务；默认 30 天恢复窗口；冻结留存会阻止实际清理 |
| deleted | 完成数据库、文件、缓存和任务清理 | 保留必要的最小审计墓碑，不再复用原租户标识与 Slug；不能通过普通恢复接口返回 |

状态变更与 TenantStateVersion 更新同事务提交，记录操作人、原因和时间，并投递缓存 / 任务通知。每分钟运行到期校准任务，但请求本身也校验有效期，不依赖定时任务准时执行。

read_only 不推进业务审批或生成新业务数据；备份、审计、清理及到期通知等维护任务可继续。suspended / deletion_pending 停止普通业务任务，执行中的任务在下一检查点停止，发布结果前再次校验状态。

删除一个租户不删除其他租户成员，也不直接删除全局用户。恢复、续订与停用使用并发和幂等控制；清理任务分步记录，失败可安全重试。

## 12. 订阅与配额模型

采用 Plan、PlanVersion、TenantSubscription、QuotaDefinition、TenantQuotaOverride、QuotaUsage、QuotaReservation 和 UsageEvent。订阅引用不可变的套餐版本，套餐修改只影响新订阅或显式迁移。

### 12.1 初始默认套餐（技术验证值，无价格含义）

| 资源码 | 默认限额 | 计量方式 |
| --- | --- | --- |
| identity.members | 50 | active 与未过期 invited 成员占用席位；disabled / removed 不占新席位 |
| files.storage_bytes | 10 GiB | 已发布、隔离、软删除文件及上传预留字节均计入；实际删除后释放 |
| jobs.exports_concurrent | 2 | 正在执行的导出任务租约 |
| jobs.imports_concurrent | 1 | 正在执行的导入任务租约 |
| api.requests_per_minute | 6000 | 租户级窗口限额，所有实例共享计量 |

GiB=1024³ 字节。配额内部使用 long，API 对外使用十进制字符串；使用显式 Unlimited 标志，不通过 -1 或 NULL 隐含无限额度。配额预留默认不允许负值。

### 12.2 生效与扣减

- 有效限额为平台安全上限与订阅值 / 已授权租户覆盖值的较小者；有效限额、来源、生效时间和版本都可查询。
- 容量类使用 SQL Server 原子条件更新或受控锁，预留、业务创建和最终占用在同一事务内完成；唯一 OperationId 防止重复扣减。
- 成员邀请默认 7 天失效，席位随有效邀请预留；接受邀请转换状态不重复扣减。暂停成员释放席位，重新启用需要重新检查容量。
- 上传先预留预计字节，完成后按实际大小核销；失败或超时的临时对象清理后才释放预留，不能仅因请求断开就忽略已存文件。
- 并发任务租约默认 2 分钟、每 30 秒续期，包含 fencing token；失去租约的任务不能发布最终结果。执行记录与资源预留配套，恢复任务不得重复占用。
- 分钟配额采用 UTC 固定分钟窗口，SQL Server 原子计数为权威；Redis 只缓存带配额版本的“当前窗口已满”结论以提前拒绝，不承担唯一放行计数，避免缓存丢失导致超额。拒绝返回 429 与窗口剩余秒数；必要时降低吞吐并返回 503，不能自动变无限制。
- 幂等回放仍计入实际 HTTP 请求窗口，但不重复扣减业务容量。固定分钟窗口允许边界短时突发，登录与高成本接口另外设置更细粒度限流。
- 套餐降级不自动删除数据或撤销既有成员；超额资源禁止新增或重新启用，保留读取和清理操作。
- 每日对账容量与资源记录，修复需记录差额及原因；配额管理、人工覆盖和 Unlimited 必须有平台权限与审计。

## 13. 缓存策略

| 数据 | 缓存与 TTL | 一致性要求 |
| --- | --- | --- |
| 模块定义、公开字典 | 单实例可用内存，正式多实例使用 Redis；10 分钟，加入 ±10% 抖动 | 变更提交后主动失效，Outbox 保证重试 |
| 租户非安全配置 | Redis，5 分钟 | key 必须包含 tenantId 与配置版本 |
| 有效权限集合 | Redis，5 分钟，带最新授权版本 | 每次请求读取主数据库授权版本；旧版本缓存不再匹配 |
| 账号、会话、租户状态及成员可用性 | 请求内缓存一次，不跨请求缓存授权结论 | 主数据库权威；关闭账号和租户无需等待 TTL |
| 普通详情 | 初期不默认缓存；模块显式启用后最长 60 秒 | key 包含租户、资源、数据 / 授权版本，不共享用户数据范围结果 |
| 不存在的公开资源 | 需要时负缓存 15 秒 | 不缓存“无权限”结论或其他租户存在性 |
| 配额、幂等、审批结果 | SQL Server 权威 | Redis 仅加速，不能成为唯一可靠存储 |

键格式：qizhu:{environment}:tenant:{tenantId}:{module}:{kind}:{identity}:v:{version}。全局数据用明确的 global 命名空间；权限结果还包含 userId / membershipId。对象统一带 schemaVersion 的 JSON，单项上限 1 MiB，不缓存凭证。

采用 cache-aside，数据库事务提交后失效；填充时校验版本以防并发回填旧值。热点并发填充使用进程内 single-flight，分布式租约用于降低击穿而非承担业务正确性。

Redis 不可用时，普通缓存回退数据库并限流；授权判断仍使用权威状态。禁止启用没有 tenantId 隔离保证的 SqlSugar 全局二级查询缓存。记录命中、失效失败、回源量及容量告警。

## 14. 可观测性规范

- 服务标识固定为 service.name=qizhu-api / qizhu-worker / qizhu-web，关联 service.version、deployment.environment 与实例标识。
- 采用 W3C traceparent / tracestate；有 Activity 时 traceId 使用 32 位十六进制 TraceId，日志另记 spanId。requestId 为当前请求标识，X-Trace-Id 为兼容保留的请求编号，不混作 TraceId。
- 日志使用结构化 UTC JSON，至少包含 timestamp、level、service、environment、eventCode、traceId、spanId、requestId、routeTemplate、statusCode、durationMs；有上下文时添加 tenantId 与脱敏 actorId。
- 不记录请求正文、Cookie、Authorization、密码、完整查询字符串和文件内容。路径记录路由模板，避免标识泄露及指标高基数。
- 普通成功请求 Information，业务拒绝按需要 Information / Warning，依赖异常和未知故障 Error；关键审计单独持久化，不能因采样丢失。
- 默认采集 HTTP 数量 / 延迟、5xx、数据库命令延迟、连接池等待、后台积压 / 失败、租户计量拒绝、缓存命中、线程池、CPU、内存和磁盘。
- 指标标签仅使用环境、服务、模块、操作模板、状态类别；不将 tenantId、userId、traceId 或原始路径作为指标标签。租户分析通过审计 / 计量查询完成。
- 正式环境应用向 Collector 发送可采样的全部追踪，由尾部采样保留 100% 错误与超过 1 秒的请求、其余保留 10%；不能在应用先丢掉 90% 后声称尾部保留全部错误。预发环境保留 100%，实施时验证链路和资源开销，生产排障临时提采样需设到期时间。
- 运行日志在线保留 30 天，追踪 7 天，指标 90 天，审计 365 天；访问受控，清理有记录。
- 告警默认：5xx 超过 1% 持续 5 分钟且至少 100 个请求；普通读取 p95 超过 500ms 持续 10 分钟；数据库 / API 不可用 1 分钟；备份滞后超过 10 分钟；磁盘剩余不足 20%；任务最老积压超过 10 分钟。

健康检查分 /health/live 与 /health/ready；前者不访问外部依赖，后者验证必要数据库与当前启用模块依赖。生产健康正文只提供概要，不公开依赖地址和凭证；就绪检查供部署探针或受控网络访问。

当前 traceId 使用 Activity.Id（可能是完整活动标识）作为返回值；实施本节时需统一为 Activity.TraceId，并保留 requestId 对现有排障流程的兼容。

## 15. 安全基线

### 15.1 密码、登录与会话

- 密码允许 15～128 个字符；不强制大小写、数字与特殊符号组合，禁止常见弱密码和已知泄露密码，允许粘贴与密码管理器。
- 使用 ASP.NET Core Identity PasswordHasher 的 IdentityV3 格式，不自行设计加密或保存明文。PBKDF2-HMAC-SHA512 的迭代次数至少 220000，在目标硬件验证 100～250ms 的哈希耗时，并支持成功登录时升级旧哈希；不能为了耗时目标降低安全下限。[OWASP 密码存储](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)。
- 账号连续 5 次失败锁定 15 分钟；登录每账号每分钟最多 5 次、每来源 IP 每分钟最多 20 次，采用可信代理地址，错误提示统一。多实例使用共享权威计数，不能靠各实例的独立内存累计。
- 平台管理员和租户管理员正式环境强制 MFA；初期采用 TOTP，并提供一次性、哈希存储的恢复码。高风险操作要求 5 分钟内完成重新验证。
- Cookie 名称 Qizhu.Session（沿用现有设置），Path=/，不设置 Domain；HttpOnly、Secure、SameSite=Lax，同源部署并实施 Antiforgery，状态变更不能由 GET 触发。空闲 30 分钟、绝对有效期 8 小时，初期不提供无限“记住我”。
- 每请求校验会话撤销与安全版本；改密、重置、账号停用和异常处置撤销相关会话。密码重置令牌默认 30 分钟、仅可使用一次。
- HTTPS 使用 TLS 1.2 或更高；生产启用 HSTS，CORS 默认关闭，必要时只允许精确的可信来源，不与带凭证的通配来源组合。

以上是项目默认策略，相关配置、自定义验证器、MFA 流程与会话存储均待身份模块实现。

### 15.2 密钥与秘密轮换

- Data Protection 使用独立环境的持久化共享密钥环；同一环境多实例共享 ApplicationName=Qizhu:{environment}，不同环境隔离。密钥环用证书或部署密钥服务加密，并配套权限最小化。
- 保护密钥默认 90 天生成新版本；旧密钥只停用新签发，保留用于读取现有数据，不能因“过期”删除。长期受保护业务数据仍引用的密钥永久保留，直至完成重新保护。[Data Protection 密钥管理](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-management?view=aspnetcore-10.0)。
- 新密钥启用前验证全部实例可读取且恢复副本存在；轮换证书时保留旧解密证书直到全部旧密钥重包装并验证恢复。
- 数据库、SMTP、对象存储与集成凭证默认每 90 天评审并轮换；使用带版本的凭证、双版本过渡和最小作用域，泄露时立即撤销，不等待周期。
- TLS 证书到期前 30 天告警；秘密不写入仓库、镜像、前端环境变量、日志或明文审计。备份同时保护密钥环及其解密材料。

### 15.3 上传、文件与导出

| 项目 | 默认规则 |
| --- | --- |
| 单文件 / 批次 | 单文件最多 20 MiB，每请求最多 5 个文件，文件总量最多 50 MiB；multipart 传输上限 52 MiB |
| 默认类型 | jpg、jpeg、png、webp、pdf、txt、csv、xlsx、docx；扩展名、MIME、文件签名及结构交叉验证 |
| 禁止类型 | 可执行文件、脚本、HTML、SVG、宏文档、普通压缩包；新增类型须定义专门的验证与预览规则 |
| OOXML 内部压缩 | 校验 xlsx / docx 容器结构；解压总量最多 100 MiB、最多 1000 个条目、压缩比最多 100:1；禁止宏和外部执行引用 |
| 文件名 / 路径 | 文件名只作为清洗后的展示文本；存储键由服务端生成，携带租户目录，不采用用户路径 |
| 安全扫描 | 正式环境采用 ClamAV 或兼容受控扫描服务，文件先进入隔离区；病毒库至少每日更新 |
| 扫描故障 | 保持隔离，不能放行；30 分钟内未完成转失败并安排清理，保留审计 |
| 存储完整性 | 记录 SHA-256、实际字节、媒体类型与状态；配额预留和发布状态由数据库控制 |
| 下载 / 预览 | 鉴权检查租户、权限与文件状态；默认附件下载，nosniff；签名下载 URL 最多 5 分钟且指向准确对象 |

后端不得只信任扩展名或 Content-Type；检验与扫描在发布之前完成。图片转码或文档预览放进受限进程，限制时间、内存和外部网络。PDF 不因扫描通过就作为同源主动内容执行。[ASP.NET Core 文件上传](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0)、[OWASP 上传规则](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html)。

### 15.4 其他基础要求

- SQL 使用参数化查询；排序与字段名使用白名单。上传 URL、Webhook 和外部请求目标防止访问本机、内网及云元数据地址，固定集成目标单独授权。
- 默认关闭生产调试接口、匿名示例与详细异常。管理端配置 CSP、frame-ancestors、nosniff 等响应头，富文本在受控规则下清洗。
- 应用数据库账户不具备结构变更权限；DbUp 使用独立发布身份。容器非 root，管理端无后端凭证。
- 依赖审计与镜像扫描纳入 CI；高风险已知问题在发布前处置并留记录，不能通过关闭审计通过检查。

## 16. RPO / RTO 与备份恢复

以下为初期正式环境目标，验证范围为 SQL Server 数据不超过 100 GiB、文件不超过 100 GiB 的基准数据集；超出范围需重新演练与定目标。

| 范围 | RPO 目标 | RTO 目标 | 必要措施 |
| --- | --- | --- | --- |
| 核心数据库、Identity、配额与审计 | 不超过 15 分钟 | 不超过 4 小时 | FULL 恢复模式，每日完全备份、每 6 小时差异、每 5 分钟日志备份，异地受保护副本 |
| 已发布文件与数据库引用 | 不超过 15 分钟 | 不超过 4 小时 | 对象版本 / 删除保护、异地复制或增量备份；验证复制滞后与数据库时间点一致性 |
| 已启用的身份保护密钥 | 不丢失任何已启用密钥 | 不超过 4 小时并随身份恢复 | 启用前持久化与恢复副本验证；保留对应解密材料 |
| Redis 缓存 | 不承诺保留缓存内容 | 30 分钟内重建目标 | 权威数据在数据库，恢复后清空旧环境与旧授权版本缓存 |
| 部署配置与模块 manifest | 不丢失已发布版本 | 不超过 4 小时 | Git、构建产物及独立受保护的部署秘密备份 |

RPO 是可接受的数据丢失时间；RTO 从确认事故到业务恢复并通过检查计算。备份计划本身不能证明目标已经达到，文件复制能力和基础设施必须通过演练确认。

备份默认保留 35 天，副本位于独立故障域，加密并限制删除；临时导出不作为备份。每月执行还原验证，每季度执行数据库、文件、密钥、部署和登录全链路恢复。

恢复步骤：停写与隔离事故环境 → 选定共同恢复时间点 → 还原数据库 → 恢复匹配文件版本及密钥 → 清理缓存 → 校验记录、引用、登录、租户隔离及任务租约 → 对账配额和 Outbox → 开放流量。外部发送、支付或集成在重放前对账，不能因数据库回退重复触发。

发布前记录可恢复备份与版本。新增列优先采用兼容式变更；破坏性结构变更单独安排窗口，不能依赖简单回滚应用二进制恢复数据库。

## 17. 性能与容量基线

这是首期验收目标，不是已完成的性能测试，也不是采购规格保证。

### 17.1 参考环境与数据

| 资源 | 验证配置 |
| --- | --- |
| API | 2 实例，每实例 4 vCPU、8 GiB RAM |
| SQL Server | 8 vCPU、32 GiB RAM、500 GiB SSD 可用空间；SQL Server 2019 / compatibility 150 兼容基线 |
| Worker | 1 实例，2 vCPU、4 GiB RAM，任务与在线接口分别限制并发 |
| Redis（启用时） | 2 vCPU、4 GiB RAM；配置内存上限和可驱逐缓存策略，可靠状态不在此唯一保存 |
| 数据集 | 100 租户、5000 成员、主要业务表总计 100 万行，包含一个占 30% 数据量的热点租户 |
| 并发与流量 | 200 在线会话；持续 100 请求/秒 30 分钟，突发 200 请求/秒 5 分钟 |
| 请求比例 | 70% 读取、20% 普通写入、10% 权限 / 配置读取；包含真实会话、租户和权限检查 |

### 17.2 验收阈值

- 普通读取 p95 ≤300ms、p99 ≤1s；普通写入 p95 ≤500ms、p99 ≤1.5s，按网关观测端到端时间计算。
- 正常负载下未知 5xx 比例 <0.1%；预期权限拒绝、配额拒绝和限流单独统计，不计入未知故障。
- API 和数据库平均 CPU ≤70%，无持续内存增长或连接池耗尽，磁盘保持至少 20% 空闲。
- 标准列表每页 20～100 项；查询有 tenant_id 条件和匹配索引，常规查询 SQL p95 ≤100ms；超过 500ms 的 SQL 进入慢查询记录。
- 导出基准为 1 万行、20 列、文本每列不超过 100 字符的 xlsx，单任务目标 60 秒内完成；单任务额外内存目标 ≤512 MiB，租户并发不超过配额。
- 超过 1 万行的导入导出使用后台任务；单任务初期最多 10 万行，格式和文件大小限制仍适用，不能为达到行数绕过上传边界。
- 测试包含冷缓存、缓存失效、单 API 实例退出、Redis 故障、数据库延迟、热点租户及跨租户拒绝；故障场景验证降级与正确性，不承诺仍达到正常延迟目标。

压测报告记录硬件、镜像与包版本、数据分布、索引、脚本、采样、吞吐、分位延迟和错误分类。未来采用 k6 编写可重复的场景；数据库与真实身份接入之前不认定容量达标。

## 18. 模块开关与扩展机制

- 每个模块提供版本化 manifest：code、version、dependencies、permissions、routes、jobs、configurationSchema。Code 使用小写 ASCII，发布后不更改身份。
- 核心模块为 identity、tenancy、system，不允许租户自行关闭。projects 是当前演示模块，仍受 Development 与显式演示开关限制，不能因为模块 manifest 自动开放生产。
- 有效启用条件为：模块已安装 ∧ 平台允许 ∧ 部署配置启用 ∧ 租户套餐授权 ∧ 租户开关启用 ∧ 所有依赖有效；任何关闭条件优先，租户不能自行提升套餐权限。
- 平台设置紧急 Kill Switch，优先级最高。受控维护恢复流程独立，不允许普通请求用参数绕过关闭条件。
- 后端拒绝被关闭模块的新请求并返回 module.disabled；前端隐藏菜单，任务不再调度。执行中任务在检查点与发布结果前复核，关闭不会删除数据。
- 禁用模块必须先处理依赖：存在仍启用的依赖者时拒绝普通关闭，或由平台执行有审计的联动停用。
- 变更开关、套餐或依赖状态同事务增加配置 / 授权版本，失效缓存并通知前端。所有授权与数据写入最终使用当前权威状态。
- 初期模块通过代码编译与 DI 装配，不动态加载上传 DLL。模块启用前需要结构变更成功，开关不是数据库迁移执行器。
- 模块接口、manifest、数据库脚本与配置均版本管理；升级采用先结构兼容、再代码、最后启用的顺序，旧配置需有迁移策略。

## 19. 环境矩阵

| 项目 | Development | CI / Test | Staging | Production |
| --- | --- | --- | --- | --- |
| 数据 | 内存示例或本地 SQL Server，纯开发数据 | 独立、可销毁 SQL Server 测试实例 | 独立库，合成或脱敏数据 | 正式 SQL Server，受控账户 |
| Redis / 文件 | 按模块使用开发实例、本地隔离目录 | 测试实例 / 临时目录，无正式资源 | 独立缓存与桶，正式配置同形 | 正式隔离资源、持久化与恢复能力 |
| 身份 / 租户头 | 演示开关允许匿名示例，X-Tenant-Id 仅用于模拟；正式登录仍用 HTTPS | 测试身份只在测试宿主注册，负向测试覆盖生产边界 | 真实身份流程、MFA、无匿名示例 | 真实身份、MFA、无匿名示例 |
| 文档 / 调试 | OpenAPI 开放，允许调试 | 测试采集接口文档，无外部开放 | OpenAPI 仅经明确授权给内部人员 | 默认关闭交互文档与演示 |
| 秘密 | 本地 user-secrets 或开发环境变量 | CI 临时受限秘密，不存正式凭证 | 独立部署秘密，独立密钥环 | 正式密钥服务 / 受保护挂载，独立密钥环 |
| 数据变更 | 手动 DbUp；CodeFirst 只对可销毁开发库显式执行 | 每次验证全量创建、增量升级和失败行为 | 发布流程执行与恢复演练 | 独立发布身份、审批发布、验证备份后执行 |
| 外部通知 | 本地接收器或沙箱 | Fake / 沙箱，禁止发送真实客户消息 | 沙箱或受控白名单 | 正式服务凭证、限额与发送审计 |
| 上传扫描 | 可模拟扫描，不能把模拟结果发布到正式存储 | 测试拒绝与扫描故障 | 真实扫描，演练隔离 | 真实扫描，不可用时不放行 |
| 监控 | 控制台日志，按需本地遥测 | 留存测试日志与报告 | 完整链路，100% 追踪 | 集中监控、告警及规范采样 |
| 模块 | 可开发未发布模块 | 按组合跑依赖和关闭测试 | 与拟发布生产组合一致 | 已验证模块和有效套餐组合 |

环境间不得共享数据库、缓存前缀、对象存储目录、会话密钥环或客户通知凭证。代码以同一不可变构建产物逐级发布，环境差异由受校验配置提供。

Staging 对应 ASPNETCORE_ENVIRONMENT=Staging，Production 对应 Production；只有 Development 可开启 EnableDevelopmentEndpoints。CI 可以创建 Development / Production 测试宿主，但测试身份与 Fake 不进入发布产物。

## 20. 实施状态与验收清单

| 能力 | 当前状态 | 仍需验收 / 扩展 |
| --- | --- | --- |
| 雪花 long、dbo、UTC、实体映射 | 已实现；真实 SQL Server 2019 完成迁移、账号与业务数据保留，前端 ID 字符串 | 多实例数据库故障注入与容量测试 |
| 项目 CRUD、并发、软删除、审计 | 已实现；真实 API 已验证 If-Match / 412 与软删除 | 30 天自动清理、其他模块业务唯一键恢复冲突 |
| 幂等与错误码 | 项目写入与审计、幂等结果同事务；重放已验证 | 提交后网络断连、长期保留清理与负载测试 |
| Identity / 租户 / 权限 | SqlSugar 自定义 Identity、Cookie、CSRF、成员资格、租户权限、TOTP 与恢复码已实现；生产管理员要求 MFA | 账号/角色/成员管理页面、完整租户生命周期与订阅 |
| Vue 管理端 | 登录、概览、租户切换、权限按钮、项目编辑/回收站、安全设置；构建和单测 | 其他公共业务模块页面与广泛浏览器验收 |
| 配额、缓存与模块开关 | 设计基线；目前仅提供模块目录查询，不是完整模块开关 | 原子预留/释放、缓存失效、Kill Switch、后台任务停用 |
| 安全与观测 | 强密码、登录计量、密钥保护、UTC JSON 日志、追踪与 live/ready | 上传扫描、集中观测、自动密钥轮换与生产配置验收 |
| 恢复、容量与环境矩阵 | 迁移前已完成原生备份并 VERIFYONLY；RPO/RTO 与吞吐仍为文档目标 | 全链路恢复演练、压测、生产环境隔离和发布验收 |

验证方式与具体变更见[实现与迁移记录](implementation-status.md)。后续建设按本规范推进，每阶段更新实现、验证证据和剩余边界，不能用文档定义代替运行验收。
