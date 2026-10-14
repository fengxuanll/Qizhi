# 开发约定

详细默认值见[数据、接口、安全与运行规范](platform-standards.md)，包括类型、命名、权限码、幂等和错误字典。该文档是后续建设目标，当前示例尚未实现全部约定。

## 命名与可读性

- 类型与方法采用清晰英文，业务说明和必要注释使用中文。
- 优先 `ProjectService`、`CreateAsync` 等直接命名，避免 `ManagerHelperUtil` 之类含糊名称。
- 方法只处理一个用例，参数和局部变量表达含义；注释说明原因或限制，不逐行翻译代码。
- 保持文件按业务模块组织。只有确实共用的能力放进 Common。

## 接口与错误

路由使用 `/api/v1/{resource}`。新建返回 201 与 Location；查询不存在返回 404；参数或业务校验返回 400。
业务规则抛出 `BusinessRuleException`，提供稳定 code 和用户可读 message。未知系统异常不要伪装成业务错误。
ApiException 区分授权、租户状态、幂等冲突和并发冲突；BusinessRuleException 映射 400，未知异常返回通用 500。新增错误需同步处理器、错误字典和客户端。
客户端使用 `code` 判断业务错误，使用 `traceId` / `requestId` 向管理员定位日志。
模型校验返回 ValidationProblemDetails，客户端通过 errors 字段展示具体字段错误。

## 时间、异步与租户

时间统一使用 UTC 的 DateTimeOffset，持久化列为 datetimeoffset(7) 并用 CHECK 限制零偏移；前端使用公共 formatUtcDateTime 按访问时区显示。用例通过 TimeProvider 获取时间，便于确定性测试。
异步方法以 Async 结尾，I/O 操作传递 CancellationToken，避免 .Result 和 .Wait()。
所有租户数据的读取、修改和删除都必须约束租户，不能仅凭业务 ID 操作。

## 配置

非敏感默认值在 appsettings.json，开发差异在 appsettings.Development.json。
生产覆盖采用环境变量，如 `Framework__DeploymentMode=MultiTenant`、`AllowedHosts=example.com`。
不要提交连接密码、令牌和私钥。设置选项时增加启动校验，错误配置应尽早失败。

## 检查与交付

提交前运行 `./scripts/check.ps1`。只为业务规则、隔离、安全边界和接口契约编写有意义的测试。
格式调整可执行 `dotnet format Qizhu.slnx --no-restore`。
普通发布：`dotnet publish src/Qizhu.Api -c Release -o artifacts/publish`。
容器构建：`docker build -t qizhu-api .`；运行示例：`docker run --rm -p 8080:8080 qizhu-api`。

CI 文件仅提供模板，仓库托管到 GitHub 后才执行。当前没有替你创建远程仓库或部署服务。


## 主键、映射、抽象与注释

业务实体与关系表使用雪花 long 主键，注入 IIdGenerator 后在创建边界分配；Domain 工厂接收已生成的 ID。禁止 Guid 主键、数据库 IDENTITY 和前端 Number 转换。安全戳及幂等键仍可以使用随机 UUID 字符串，它们不是数据库主键。

持久化类型继承 EntityRecord，可编辑租户实体继承 AuditedTenantRecord。通过 [SugarTable("dbo.模块_表")] / [SugarColumn] 显式映射；查询使用 Queryable，新增使用 Insertable，更新使用 Updateable.SetColumns，删除使用 Deleteable。更新条件必须包括 tenant_id 与需要的 rowversion；禁止先读版本后进行无条件更新。

只有迁移 DDL/数据转换、备份恢复维护及经论证的数据库特殊能力允许专门 SQL；禁止业务仓储拼接 SQL、动态接收字段名或全局关闭租户过滤。当前业务访问已移除 Ado.SqlQuery/ExecuteCommand，Ado 仅用于事务控制和请求取消。

为外部依赖、持久化和跨模块能力优先定义接口，保持 Application 不依赖 SqlSugar；通用字段抽象为基类，业务差异留在具体模型，不建立任意跨租户的万能仓储。接口、公共基类和关键规则用中文 XML 注释，写明语义、单位、UTC、幂等/并发、失败行为与安全边界；普通 Identity 接口实现避免逐行重复注释。

启动配置 Snowflake:WorkerId，生产实例使用不同节点号，节点水位不能重置。旧主键迁移后必须同步资源 URL、租户配置与 API 类型；禁止修改已经执行过的迁移文件。生产备份应进入有权限和留存管理的备份系统，本地 work 备份仅为当前迁移验证。
