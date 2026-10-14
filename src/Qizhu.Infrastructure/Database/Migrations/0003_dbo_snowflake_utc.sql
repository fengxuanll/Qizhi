SET XACT_ABORT ON;
-- 0003：GUID 历史表 -> dbo 业务表 + bigint 雪花 ID；迁移节点 1023 专用。
-- DbUp 对整个脚本使用事务，先存储逐表完整 JSON 快照并核对行数，成功复制后才清理旧表。
-- 身份密码、成员关系、软删除与审计历史保留；旧会话撤销，旧幂等响应仅归档（契约已改变）。
-- 发布前须执行 SQL Server 原生备份；本迁移没有改变数据库默认 collation。
DECLARE @now datetimeoffset(7)=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00');
DECLARE @ms bigint=DATEDIFF_BIG(millisecond,'2025-01-01',SYSUTCDATETIME());
DECLARE @next bigint=0;
CREATE TABLE dbo.system_snowflake_nodes(worker_id bigint NOT NULL PRIMARY KEY, last_timestamp bigint NOT NULL);
CREATE TABLE dbo.system_legacy_ids(id bigint NOT NULL PRIMARY KEY, legacy_id uniqueidentifier NOT NULL UNIQUE);
CREATE TABLE dbo.system_legacy_archive(
 id bigint NOT NULL PRIMARY KEY, source_table nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL UNIQUE,
 row_count bigint NOT NULL, payload nvarchar(max) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
 archived_at datetimeoffset(7) NOT NULL CHECK(DATEPART(TZOFFSET,archived_at)=0));
CREATE TABLE #map(legacy_id uniqueidentifier PRIMARY KEY, n bigint NOT NULL UNIQUE);
INSERT INTO #map SELECT legacy_id,ROW_NUMBER() OVER(ORDER BY legacy_id) FROM (SELECT id AS legacy_id FROM [identity].[users] WHERE id IS NOT NULL
UNION
SELECT id AS legacy_id FROM [identity].[platform_roles] WHERE id IS NOT NULL
UNION
SELECT user_id AS legacy_id FROM [identity].[platform_user_roles] WHERE user_id IS NOT NULL
UNION
SELECT role_id AS legacy_id FROM [identity].[platform_user_roles] WHERE role_id IS NOT NULL
UNION
SELECT user_id AS legacy_id FROM [identity].[user_claims] WHERE user_id IS NOT NULL
UNION
SELECT user_id AS legacy_id FROM [identity].[user_tokens] WHERE user_id IS NOT NULL
UNION
SELECT id AS legacy_id FROM [tenancy].[tenants] WHERE id IS NOT NULL
UNION
SELECT id AS legacy_id FROM [tenancy].[tenant_memberships] WHERE id IS NOT NULL
UNION
SELECT tenant_id AS legacy_id FROM [tenancy].[tenant_memberships] WHERE tenant_id IS NOT NULL
UNION
SELECT user_id AS legacy_id FROM [tenancy].[tenant_memberships] WHERE user_id IS NOT NULL
UNION
SELECT id AS legacy_id FROM [tenancy].[tenant_roles] WHERE id IS NOT NULL
UNION
SELECT tenant_id AS legacy_id FROM [tenancy].[tenant_roles] WHERE tenant_id IS NOT NULL
UNION
SELECT tenant_id AS legacy_id FROM [tenancy].[membership_roles] WHERE tenant_id IS NOT NULL
UNION
SELECT membership_id AS legacy_id FROM [tenancy].[membership_roles] WHERE membership_id IS NOT NULL
UNION
SELECT role_id AS legacy_id FROM [tenancy].[membership_roles] WHERE role_id IS NOT NULL
UNION
SELECT tenant_id AS legacy_id FROM [tenancy].[role_permissions] WHERE tenant_id IS NOT NULL
UNION
SELECT role_id AS legacy_id FROM [tenancy].[role_permissions] WHERE role_id IS NOT NULL
UNION
SELECT id AS legacy_id FROM [identity].[sessions] WHERE id IS NOT NULL
UNION
SELECT user_id AS legacy_id FROM [identity].[sessions] WHERE user_id IS NOT NULL
UNION
SELECT tenant_id AS legacy_id FROM [identity].[sessions] WHERE tenant_id IS NOT NULL
UNION
SELECT membership_id AS legacy_id FROM [identity].[sessions] WHERE membership_id IS NOT NULL
UNION
SELECT id AS legacy_id FROM [projects].[projects] WHERE id IS NOT NULL
UNION
SELECT tenant_id AS legacy_id FROM [projects].[projects] WHERE tenant_id IS NOT NULL
UNION
SELECT created_by AS legacy_id FROM [projects].[projects] WHERE created_by IS NOT NULL
UNION
SELECT updated_by AS legacy_id FROM [projects].[projects] WHERE updated_by IS NOT NULL
UNION
SELECT deleted_by AS legacy_id FROM [projects].[projects] WHERE deleted_by IS NOT NULL
UNION
SELECT event_id AS legacy_id FROM [audit].[events] WHERE event_id IS NOT NULL
UNION
SELECT tenant_id AS legacy_id FROM [audit].[events] WHERE tenant_id IS NOT NULL
UNION
SELECT actor_id AS legacy_id FROM [audit].[events] WHERE actor_id IS NOT NULL
UNION
SELECT resource_id AS legacy_id FROM [audit].[events] WHERE resource_id IS NOT NULL
UNION
SELECT tenant_id AS legacy_id FROM [system].[idempotency_records] WHERE tenant_id IS NOT NULL
UNION
SELECT actor_id AS legacy_id FROM [system].[idempotency_records] WHERE actor_id IS NOT NULL) AS ids;
INSERT INTO dbo.system_legacy_ids SELECT ((@ms+((n)-1)/4096)*4194304+4190208+((n)-1)%4096),legacy_id FROM #map;
SET @next=(SELECT COUNT_BIG(*) FROM #map);
CREATE TABLE [dbo].[identity_users] (
    id bigint NOT NULL CONSTRAINT pk_v3_identity_users PRIMARY KEY,
    user_name nvarchar(64) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    normalized_user_name nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    display_name nvarchar(100) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    password_hash nvarchar(1000) COLLATE Latin1_General_100_BIN2 NULL,
    security_stamp nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    concurrency_stamp nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    email nvarchar(256) COLLATE Latin1_General_100_CI_AS_SC NULL,
    normalized_email nvarchar(256) COLLATE Latin1_General_100_BIN2 NULL,
    email_confirmed bit NOT NULL DEFAULT 0,
    two_factor_enabled bit NOT NULL DEFAULT 0,
    authenticator_key nvarchar(256) COLLATE Latin1_General_100_BIN2 NULL,
    recovery_codes nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    access_failed_count int NOT NULL DEFAULT 0,
    lockout_enabled bit NOT NULL DEFAULT 1,
    lockout_end datetimeoffset(7) NULL,
    is_enabled bit NOT NULL DEFAULT 1,
    created_at datetimeoffset(7) NOT NULL,
    updated_at datetimeoffset(7) NOT NULL,
    row_version rowversion NOT NULL,
    CONSTRAINT uq_v3_identity_users_name UNIQUE(normalized_user_name)
);
CREATE UNIQUE INDEX uq_v3_identity_users_verified_email ON [dbo].[identity_users](normalized_email) WHERE email_confirmed=1 AND normalized_email IS NOT NULL;

CREATE TABLE [dbo].[identity_platform_roles] (
    id bigint NOT NULL CONSTRAINT pk_v3_identity_platform_roles PRIMARY KEY,
    name nvarchar(128) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    normalized_name nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL UNIQUE,
    concurrency_stamp nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL
);
CREATE TABLE [dbo].[identity_platform_user_roles] (
    id bigint NOT NULL CONSTRAINT pk_v3_identity_platform_user_roles PRIMARY KEY,
    user_id bigint NOT NULL REFERENCES [dbo].[identity_users](id),
    role_id bigint NOT NULL REFERENCES [dbo].[identity_platform_roles](id),
    CONSTRAINT uq_v3_identity_platform_user_roles UNIQUE(user_id,role_id)
);
CREATE TABLE [dbo].[identity_user_claims] (
    id bigint CONSTRAINT pk_v3_identity_user_claims PRIMARY KEY,
    user_id bigint NOT NULL REFERENCES [dbo].[identity_users](id),
    claim_type nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    claim_value nvarchar(1000) COLLATE Latin1_General_100_BIN2 NOT NULL
);
CREATE TABLE [dbo].[identity_user_tokens] (
    id bigint NOT NULL CONSTRAINT pk_v3_identity_user_tokens PRIMARY KEY,
    user_id bigint NOT NULL REFERENCES [dbo].[identity_users](id),
    login_provider nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    name nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    value nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    CONSTRAINT uq_v3_identity_user_tokens UNIQUE(user_id,login_provider,name)
);

CREATE TABLE [dbo].[tenancy_tenants] (
    id bigint NOT NULL CONSTRAINT pk_v3_tenancy_tenants PRIMARY KEY,
    slug varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL UNIQUE,
    name nvarchar(100) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    state varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    expires_at datetimeoffset(7) NOT NULL,
    state_version bigint NOT NULL DEFAULT 1,
    authorization_version bigint NOT NULL DEFAULT 1,
    created_at datetimeoffset(7) NOT NULL,
    row_version rowversion NOT NULL,
    CONSTRAINT ck_v3_tenancy_tenants_state CHECK(state IN ('provisioning','trial','active','read_only','suspended','deletion_pending','deleted'))
);
CREATE TABLE [dbo].[tenancy_tenant_memberships] (
    id bigint NOT NULL CONSTRAINT pk_v3_tenancy_tenant_memberships PRIMARY KEY,
    tenant_id bigint NOT NULL REFERENCES [dbo].[tenancy_tenants](id),
    user_id bigint NOT NULL REFERENCES [dbo].[identity_users](id),
    status varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    display_name nvarchar(100) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    created_at datetimeoffset(7) NOT NULL,
    CONSTRAINT uq_v3_tenancy_membership_user UNIQUE(tenant_id,user_id),
    CONSTRAINT uq_v3_tenancy_membership_tenant_id UNIQUE(tenant_id,id)
);
CREATE TABLE [dbo].[tenancy_tenant_roles] (
    id bigint NOT NULL CONSTRAINT pk_v3_tenancy_tenant_roles PRIMARY KEY,
    tenant_id bigint NOT NULL REFERENCES [dbo].[tenancy_tenants](id),
    code varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    name nvarchar(100) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    CONSTRAINT uq_v3_tenancy_roles_code UNIQUE(tenant_id,code),
    CONSTRAINT uq_v3_tenancy_roles_tenant_id UNIQUE(tenant_id,id)
);
CREATE TABLE [dbo].[tenancy_membership_roles] (
    id bigint NOT NULL CONSTRAINT pk_v3_tenancy_membership_roles PRIMARY KEY,
    tenant_id bigint NOT NULL,
    membership_id bigint NOT NULL,
    role_id bigint NOT NULL,
    CONSTRAINT uq_v3_tenancy_membership_roles UNIQUE(tenant_id,membership_id,role_id),
    FOREIGN KEY(tenant_id,membership_id) REFERENCES [dbo].[tenancy_tenant_memberships](tenant_id,id),
    FOREIGN KEY(tenant_id,role_id) REFERENCES [dbo].[tenancy_tenant_roles](tenant_id,id)
);
CREATE TABLE [dbo].[system_permissions] (
    id bigint NOT NULL CONSTRAINT pk_v3_system_permissions PRIMARY KEY,
    code varchar(128) COLLATE Latin1_General_100_BIN2 CONSTRAINT uq_v3_system_permissions UNIQUE,
    name nvarchar(100) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    scope varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL
);
CREATE TABLE [dbo].[tenancy_role_permissions] (
    id bigint NOT NULL CONSTRAINT pk_v3_tenancy_role_permissions PRIMARY KEY,
    tenant_id bigint NOT NULL,
    role_id bigint NOT NULL,
    permission_code varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL REFERENCES [dbo].[system_permissions](code),
    CONSTRAINT uq_v3_tenancy_role_permissions UNIQUE(tenant_id,role_id,permission_code),
    FOREIGN KEY(tenant_id,role_id) REFERENCES [dbo].[tenancy_tenant_roles](tenant_id,id)
);

CREATE TABLE [dbo].[identity_sessions] (
    id bigint CONSTRAINT pk_v3_identity_sessions PRIMARY KEY,
    user_id bigint NOT NULL REFERENCES [dbo].[identity_users](id),
    tenant_id bigint NULL,
    membership_id bigint NULL,
    security_stamp nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_at datetimeoffset(7) NOT NULL,
    last_seen_at datetimeoffset(7) NOT NULL,
    expires_at datetimeoffset(7) NOT NULL,
    is_revoked bit NOT NULL DEFAULT 1,
    FOREIGN KEY(tenant_id,membership_id) REFERENCES [dbo].[tenancy_tenant_memberships](tenant_id,id)
);
CREATE INDEX ix_v3_identity_sessions_user ON [dbo].[identity_sessions](user_id,is_revoked);

CREATE TABLE [dbo].[projects] (
    id bigint CONSTRAINT pk_v3_projects_projects PRIMARY KEY,
    tenant_id bigint NOT NULL REFERENCES [dbo].[tenancy_tenants](id),
    name nvarchar(100) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    created_at datetimeoffset(7) NOT NULL,
    created_by bigint NULL REFERENCES [dbo].[identity_users](id),
    updated_at datetimeoffset(7) NOT NULL,
    updated_by bigint NULL REFERENCES [dbo].[identity_users](id),
    is_deleted bit NOT NULL DEFAULT 0,
    deleted_at datetimeoffset(7) NULL,
    deleted_by bigint NULL REFERENCES [dbo].[identity_users](id),
    row_version rowversion NOT NULL,
    CONSTRAINT uq_v3_projects_projects_tenant_id UNIQUE(tenant_id,id),
    CONSTRAINT ck_v3_projects_projects_deleted CHECK((is_deleted=0 AND deleted_at IS NULL) OR (is_deleted=1 AND deleted_at IS NOT NULL))
);
CREATE INDEX ix_v3_projects_projects_list ON [dbo].[projects](tenant_id,is_deleted,created_at DESC,id) INCLUDE(name,updated_at,row_version);

CREATE TABLE [dbo].[audit_events] (
    id bigint CONSTRAINT pk_v3_audit_events PRIMARY KEY,
    tenant_id bigint NULL,
    actor_id bigint NULL,
    action varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    resource_id bigint NULL,
    summary nvarchar(1000) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    occurred_at datetimeoffset(7) NOT NULL,
    request_id varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    trace_id varchar(64) COLLATE Latin1_General_100_BIN2 NULL
);
CREATE INDEX ix_v3_audit_events_tenant_time ON [dbo].[audit_events](tenant_id,occurred_at DESC,id);
CREATE TABLE [dbo].[system_idempotency_records] (
    id bigint NOT NULL CONSTRAINT pk_v3_system_idempotency_records PRIMARY KEY,
    tenant_id bigint NOT NULL,
    actor_id bigint NOT NULL,
    operation varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    key_hash binary(32) NOT NULL,
    request_hash binary(32) NOT NULL,
    status_code int NOT NULL,
    response_json nvarchar(max) COLLATE Latin1_General_100_CI_AS_SC NULL,
    location nvarchar(500) COLLATE Latin1_General_100_BIN2 NULL,
    etag varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    expires_at datetimeoffset(7) NOT NULL,
    CONSTRAINT uq_v3_system_idempotency UNIQUE(tenant_id,actor_id,operation,key_hash)
);

-- 已执行的 0001 不再修改；新增安全计量与完整审计字段。
CREATE TABLE [dbo].[identity_login_windows] (
    id bigint NOT NULL CONSTRAINT pk_v3_identity_login_windows PRIMARY KEY,
    kind varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    key_hash binary(32) NOT NULL,
    window_start datetimeoffset(7) NOT NULL,
    request_count int NOT NULL,
    CONSTRAINT uq_v3_identity_login_windows UNIQUE(kind,key_hash,window_start)
);
ALTER TABLE [dbo].[audit_events] ADD
    actor_type varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT 'user',
    resource_type varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT 'projects',
    result varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT 'success';

SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'identity.users',(SELECT COUNT_BIG(*) FROM [identity].[users]),COALESCE((SELECT * FROM [identity].[users] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[identity_users](id,user_name,normalized_user_name,display_name,password_hash,security_stamp,concurrency_stamp,email,normalized_email,email_confirmed,two_factor_enabled,authenticator_key,recovery_codes,access_failed_count,lockout_enabled,lockout_end,is_enabled,created_at,updated_at)
SELECT (SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.id),s.user_name,s.normalized_user_name,s.display_name,s.password_hash,s.security_stamp,s.concurrency_stamp,s.email,s.normalized_email,s.email_confirmed,s.two_factor_enabled,s.authenticator_key,s.recovery_codes,s.access_failed_count,s.lockout_enabled,SWITCHOFFSET(s.lockout_end,'+00:00'),s.is_enabled,SWITCHOFFSET(s.created_at,'+00:00'),SWITCHOFFSET(s.updated_at,'+00:00') FROM [identity].[users] s;
IF (SELECT COUNT_BIG(*) FROM [dbo].[identity_users]) <> (SELECT COUNT_BIG(*) FROM [identity].[users]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'identity.platform_roles',(SELECT COUNT_BIG(*) FROM [identity].[platform_roles]),COALESCE((SELECT * FROM [identity].[platform_roles] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[identity_platform_roles](id,name,normalized_name,concurrency_stamp)
SELECT (SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.id),s.name,s.normalized_name,s.concurrency_stamp FROM [identity].[platform_roles] s;
IF (SELECT COUNT_BIG(*) FROM [dbo].[identity_platform_roles]) <> (SELECT COUNT_BIG(*) FROM [identity].[platform_roles]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'identity.platform_user_roles',(SELECT COUNT_BIG(*) FROM [identity].[platform_user_roles]),COALESCE((SELECT * FROM [identity].[platform_user_roles] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[identity_platform_user_roles](id,user_id,role_id)
SELECT ((@ms+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)/4096)*4194304+4190208+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)%4096),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.user_id),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.role_id) FROM [identity].[platform_user_roles] s;
SET @next=@next+(SELECT COUNT_BIG(*) FROM [identity].[platform_user_roles]);
IF (SELECT COUNT_BIG(*) FROM [dbo].[identity_platform_user_roles]) <> (SELECT COUNT_BIG(*) FROM [identity].[platform_user_roles]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'identity.user_claims',(SELECT COUNT_BIG(*) FROM [identity].[user_claims]),COALESCE((SELECT * FROM [identity].[user_claims] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[identity_user_claims](id,user_id,claim_type,claim_value)
SELECT ((@ms+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)/4096)*4194304+4190208+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)%4096),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.user_id),s.claim_type,s.claim_value FROM [identity].[user_claims] s;
SET @next=@next+(SELECT COUNT_BIG(*) FROM [identity].[user_claims]);
IF (SELECT COUNT_BIG(*) FROM [dbo].[identity_user_claims]) <> (SELECT COUNT_BIG(*) FROM [identity].[user_claims]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'identity.user_tokens',(SELECT COUNT_BIG(*) FROM [identity].[user_tokens]),COALESCE((SELECT * FROM [identity].[user_tokens] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[identity_user_tokens](id,user_id,login_provider,name,value)
SELECT ((@ms+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)/4096)*4194304+4190208+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)%4096),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.user_id),s.login_provider,s.name,s.value FROM [identity].[user_tokens] s;
SET @next=@next+(SELECT COUNT_BIG(*) FROM [identity].[user_tokens]);
IF (SELECT COUNT_BIG(*) FROM [dbo].[identity_user_tokens]) <> (SELECT COUNT_BIG(*) FROM [identity].[user_tokens]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'tenancy.tenants',(SELECT COUNT_BIG(*) FROM [tenancy].[tenants]),COALESCE((SELECT * FROM [tenancy].[tenants] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[tenancy_tenants](id,slug,name,state,expires_at,state_version,authorization_version,created_at)
SELECT (SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.id),s.slug,s.name,s.state,SWITCHOFFSET(s.expires_at,'+00:00'),s.state_version,s.authorization_version,SWITCHOFFSET(s.created_at,'+00:00') FROM [tenancy].[tenants] s;
IF (SELECT COUNT_BIG(*) FROM [dbo].[tenancy_tenants]) <> (SELECT COUNT_BIG(*) FROM [tenancy].[tenants]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'tenancy.tenant_memberships',(SELECT COUNT_BIG(*) FROM [tenancy].[tenant_memberships]),COALESCE((SELECT * FROM [tenancy].[tenant_memberships] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[tenancy_tenant_memberships](id,tenant_id,user_id,status,display_name,created_at)
SELECT (SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.id),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.tenant_id),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.user_id),s.status,s.display_name,SWITCHOFFSET(s.created_at,'+00:00') FROM [tenancy].[tenant_memberships] s;
IF (SELECT COUNT_BIG(*) FROM [dbo].[tenancy_tenant_memberships]) <> (SELECT COUNT_BIG(*) FROM [tenancy].[tenant_memberships]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'tenancy.tenant_roles',(SELECT COUNT_BIG(*) FROM [tenancy].[tenant_roles]),COALESCE((SELECT * FROM [tenancy].[tenant_roles] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[tenancy_tenant_roles](id,tenant_id,code,name)
SELECT (SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.id),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.tenant_id),s.code,s.name FROM [tenancy].[tenant_roles] s;
IF (SELECT COUNT_BIG(*) FROM [dbo].[tenancy_tenant_roles]) <> (SELECT COUNT_BIG(*) FROM [tenancy].[tenant_roles]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'tenancy.membership_roles',(SELECT COUNT_BIG(*) FROM [tenancy].[membership_roles]),COALESCE((SELECT * FROM [tenancy].[membership_roles] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[tenancy_membership_roles](id,tenant_id,membership_id,role_id)
SELECT ((@ms+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)/4096)*4194304+4190208+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)%4096),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.tenant_id),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.membership_id),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.role_id) FROM [tenancy].[membership_roles] s;
SET @next=@next+(SELECT COUNT_BIG(*) FROM [tenancy].[membership_roles]);
IF (SELECT COUNT_BIG(*) FROM [dbo].[tenancy_membership_roles]) <> (SELECT COUNT_BIG(*) FROM [tenancy].[membership_roles]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'system.permissions',(SELECT COUNT_BIG(*) FROM [system].[permissions]),COALESCE((SELECT * FROM [system].[permissions] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[system_permissions](id,code,name,scope)
SELECT ((@ms+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)/4096)*4194304+4190208+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)%4096),s.code,s.name,s.scope FROM [system].[permissions] s;
SET @next=@next+(SELECT COUNT_BIG(*) FROM [system].[permissions]);
IF (SELECT COUNT_BIG(*) FROM [dbo].[system_permissions]) <> (SELECT COUNT_BIG(*) FROM [system].[permissions]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'tenancy.role_permissions',(SELECT COUNT_BIG(*) FROM [tenancy].[role_permissions]),COALESCE((SELECT * FROM [tenancy].[role_permissions] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[tenancy_role_permissions](id,tenant_id,role_id,permission_code)
SELECT ((@ms+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)/4096)*4194304+4190208+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)%4096),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.tenant_id),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.role_id),s.permission_code FROM [tenancy].[role_permissions] s;
SET @next=@next+(SELECT COUNT_BIG(*) FROM [tenancy].[role_permissions]);
IF (SELECT COUNT_BIG(*) FROM [dbo].[tenancy_role_permissions]) <> (SELECT COUNT_BIG(*) FROM [tenancy].[role_permissions]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'identity.sessions',(SELECT COUNT_BIG(*) FROM [identity].[sessions]),COALESCE((SELECT * FROM [identity].[sessions] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[identity_sessions](id,user_id,tenant_id,membership_id,security_stamp,created_at,last_seen_at,expires_at,is_revoked)
SELECT (SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.id),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.user_id),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.tenant_id),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.membership_id),s.security_stamp,SWITCHOFFSET(s.created_at,'+00:00'),SWITCHOFFSET(s.last_seen_at,'+00:00'),SWITCHOFFSET(s.expires_at,'+00:00'),CAST(1 AS bit) FROM [identity].[sessions] s;
IF (SELECT COUNT_BIG(*) FROM [dbo].[identity_sessions]) <> (SELECT COUNT_BIG(*) FROM [identity].[sessions]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'projects.projects',(SELECT COUNT_BIG(*) FROM [projects].[projects]),COALESCE((SELECT * FROM [projects].[projects] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[projects](id,tenant_id,name,created_at,created_by,updated_at,updated_by,is_deleted,deleted_at,deleted_by)
SELECT (SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.id),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.tenant_id),s.name,SWITCHOFFSET(s.created_at,'+00:00'),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.created_by),SWITCHOFFSET(s.updated_at,'+00:00'),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.updated_by),s.is_deleted,SWITCHOFFSET(s.deleted_at,'+00:00'),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.deleted_by) FROM [projects].[projects] s;
IF (SELECT COUNT_BIG(*) FROM [dbo].[projects]) <> (SELECT COUNT_BIG(*) FROM [projects].[projects]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'audit.events',(SELECT COUNT_BIG(*) FROM [audit].[events]),COALESCE((SELECT * FROM [audit].[events] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[audit_events](id,tenant_id,actor_id,action,resource_id,summary,occurred_at,request_id,trace_id,actor_type,resource_type,result)
SELECT (SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.event_id),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.tenant_id),(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.actor_id),s.action,(SELECT id FROM dbo.system_legacy_ids WHERE legacy_id=s.resource_id),s.summary,SWITCHOFFSET(s.occurred_at,'+00:00'),s.request_id,s.trace_id,s.actor_type,s.resource_type,s.result FROM [audit].[events] s;
IF (SELECT COUNT_BIG(*) FROM [dbo].[audit_events]) <> (SELECT COUNT_BIG(*) FROM [audit].[events]) THROW 51000,'Migration row count mismatch',1;
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'system.idempotency_records',(SELECT COUNT_BIG(*) FROM [system].[idempotency_records]),COALESCE((SELECT * FROM [system].[idempotency_records] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
SET @next=@next+1;
INSERT INTO dbo.system_legacy_archive VALUES(((@ms+((@next)-1)/4096)*4194304+4190208+((@next)-1)%4096),'identity.login_windows',(SELECT COUNT_BIG(*) FROM [identity].[login_windows]),COALESCE((SELECT * FROM [identity].[login_windows] FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'),@now);
INSERT INTO [dbo].[identity_login_windows](id,kind,key_hash,window_start,request_count)
SELECT ((@ms+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)/4096)*4194304+4190208+((@next+ROW_NUMBER() OVER(ORDER BY (SELECT NULL)))-1)%4096),s.kind,s.key_hash,SWITCHOFFSET(s.window_start,'+00:00'),s.request_count FROM [identity].[login_windows] s;
SET @next=@next+(SELECT COUNT_BIG(*) FROM [identity].[login_windows]);
IF (SELECT COUNT_BIG(*) FROM [dbo].[identity_login_windows]) <> (SELECT COUNT_BIG(*) FROM [identity].[login_windows]) THROW 51000,'Migration row count mismatch',1;
ALTER TABLE [dbo].[identity_users] ADD CONSTRAINT ck_v3_utc_identity_users_lockout_end CHECK(lockout_end IS NULL OR DATEPART(TZOFFSET,lockout_end)=0);
ALTER TABLE [dbo].[identity_users] ADD CONSTRAINT ck_v3_utc_identity_users_created_at CHECK(created_at IS NULL OR DATEPART(TZOFFSET,created_at)=0);
ALTER TABLE [dbo].[identity_users] ADD CONSTRAINT ck_v3_utc_identity_users_updated_at CHECK(updated_at IS NULL OR DATEPART(TZOFFSET,updated_at)=0);
ALTER TABLE [dbo].[tenancy_tenants] ADD CONSTRAINT ck_v3_utc_tenancy_tenants_expires_at CHECK(expires_at IS NULL OR DATEPART(TZOFFSET,expires_at)=0);
ALTER TABLE [dbo].[tenancy_tenants] ADD CONSTRAINT ck_v3_utc_tenancy_tenants_created_at CHECK(created_at IS NULL OR DATEPART(TZOFFSET,created_at)=0);
ALTER TABLE [dbo].[tenancy_tenant_memberships] ADD CONSTRAINT ck_v3_utc_tenancy_tenant_memberships_created_at CHECK(created_at IS NULL OR DATEPART(TZOFFSET,created_at)=0);
ALTER TABLE [dbo].[identity_sessions] ADD CONSTRAINT ck_v3_utc_identity_sessions_created_at CHECK(created_at IS NULL OR DATEPART(TZOFFSET,created_at)=0);
ALTER TABLE [dbo].[identity_sessions] ADD CONSTRAINT ck_v3_utc_identity_sessions_last_seen_at CHECK(last_seen_at IS NULL OR DATEPART(TZOFFSET,last_seen_at)=0);
ALTER TABLE [dbo].[identity_sessions] ADD CONSTRAINT ck_v3_utc_identity_sessions_expires_at CHECK(expires_at IS NULL OR DATEPART(TZOFFSET,expires_at)=0);
ALTER TABLE [dbo].[projects] ADD CONSTRAINT ck_v3_utc_projects_created_at CHECK(created_at IS NULL OR DATEPART(TZOFFSET,created_at)=0);
ALTER TABLE [dbo].[projects] ADD CONSTRAINT ck_v3_utc_projects_updated_at CHECK(updated_at IS NULL OR DATEPART(TZOFFSET,updated_at)=0);
ALTER TABLE [dbo].[projects] ADD CONSTRAINT ck_v3_utc_projects_deleted_at CHECK(deleted_at IS NULL OR DATEPART(TZOFFSET,deleted_at)=0);
ALTER TABLE [dbo].[audit_events] ADD CONSTRAINT ck_v3_utc_audit_events_occurred_at CHECK(occurred_at IS NULL OR DATEPART(TZOFFSET,occurred_at)=0);
ALTER TABLE [dbo].[system_idempotency_records] ADD CONSTRAINT ck_v3_utc_system_idempotency_records_expires_at CHECK(expires_at IS NULL OR DATEPART(TZOFFSET,expires_at)=0);
ALTER TABLE [dbo].[identity_login_windows] ADD CONSTRAINT ck_v3_utc_identity_login_windows_window_start CHECK(window_start IS NULL OR DATEPART(TZOFFSET,window_start)=0);
IF EXISTS(SELECT 1 FROM dbo.system_legacy_archive a WHERE ISJSON(a.payload)<>1 OR a.row_count<>(SELECT COUNT_BIG(*) FROM OPENJSON(a.payload))) THROW 51001,'Archive row count mismatch',1;
DROP TABLE [identity].[login_windows];
DROP TABLE [system].[idempotency_records];
DROP TABLE [audit].[events];
DROP TABLE [projects].[projects];
DROP TABLE [identity].[sessions];
DROP TABLE [tenancy].[role_permissions];
DROP TABLE [system].[permissions];
DROP TABLE [tenancy].[membership_roles];
DROP TABLE [tenancy].[tenant_roles];
DROP TABLE [tenancy].[tenant_memberships];
DROP TABLE [tenancy].[tenants];
DROP TABLE [identity].[user_tokens];
DROP TABLE [identity].[user_claims];
DROP TABLE [identity].[platform_user_roles];
DROP TABLE [identity].[platform_roles];
DROP TABLE [identity].[users];
INSERT INTO dbo.system_snowflake_nodes VALUES(1023,@ms+(@next-1)/4096);
DROP TABLE #map;
