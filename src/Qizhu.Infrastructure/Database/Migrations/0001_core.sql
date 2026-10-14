-- SQL Server 2019 / compatibility level 150. 不修改数据库默认 collation。
-- 所有文本列显式声明排序规则，脚本只创建 Qizhu 自有对象。
IF SCHEMA_ID('identity') IS NULL EXEC('CREATE SCHEMA [identity]');
IF SCHEMA_ID('tenancy') IS NULL EXEC('CREATE SCHEMA [tenancy]');
IF SCHEMA_ID('projects') IS NULL EXEC('CREATE SCHEMA [projects]');
IF SCHEMA_ID('audit') IS NULL EXEC('CREATE SCHEMA [audit]');
IF SCHEMA_ID('system') IS NULL EXEC('CREATE SCHEMA [system]');

CREATE TABLE [identity].[users] (
    id uniqueidentifier NOT NULL CONSTRAINT pk_identity_users PRIMARY KEY NONCLUSTERED,
    storage_sequence bigint IDENTITY NOT NULL,
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
    CONSTRAINT uq_identity_users_name UNIQUE(normalized_user_name)
);
CREATE UNIQUE CLUSTERED INDEX cx_identity_users_sequence ON [identity].[users](storage_sequence);
CREATE UNIQUE INDEX uq_identity_users_verified_email ON [identity].[users](normalized_email) WHERE email_confirmed=1 AND normalized_email IS NOT NULL;

CREATE TABLE [identity].[platform_roles] (
    id uniqueidentifier NOT NULL CONSTRAINT pk_identity_platform_roles PRIMARY KEY,
    name nvarchar(128) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    normalized_name nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL UNIQUE,
    concurrency_stamp nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL
);
CREATE TABLE [identity].[platform_user_roles] (
    user_id uniqueidentifier NOT NULL REFERENCES [identity].[users](id),
    role_id uniqueidentifier NOT NULL REFERENCES [identity].[platform_roles](id),
    CONSTRAINT pk_identity_platform_user_roles PRIMARY KEY(user_id,role_id)
);
CREATE TABLE [identity].[user_claims] (
    id bigint IDENTITY CONSTRAINT pk_identity_user_claims PRIMARY KEY,
    user_id uniqueidentifier NOT NULL REFERENCES [identity].[users](id),
    claim_type nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    claim_value nvarchar(1000) COLLATE Latin1_General_100_BIN2 NOT NULL
);
CREATE TABLE [identity].[user_tokens] (
    user_id uniqueidentifier NOT NULL REFERENCES [identity].[users](id),
    login_provider nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    name nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    value nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    CONSTRAINT pk_identity_user_tokens PRIMARY KEY(user_id,login_provider,name)
);

CREATE TABLE [tenancy].[tenants] (
    id uniqueidentifier NOT NULL CONSTRAINT pk_tenancy_tenants PRIMARY KEY NONCLUSTERED,
    storage_sequence bigint IDENTITY NOT NULL,
    slug varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL UNIQUE,
    name nvarchar(100) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    state varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    expires_at datetimeoffset(7) NOT NULL,
    state_version bigint NOT NULL DEFAULT 1,
    authorization_version bigint NOT NULL DEFAULT 1,
    created_at datetimeoffset(7) NOT NULL,
    row_version rowversion NOT NULL,
    CONSTRAINT ck_tenancy_tenants_state CHECK(state IN ('provisioning','trial','active','read_only','suspended','deletion_pending','deleted'))
);
CREATE UNIQUE CLUSTERED INDEX cx_tenancy_tenants_sequence ON [tenancy].[tenants](storage_sequence);
CREATE TABLE [tenancy].[tenant_memberships] (
    id uniqueidentifier NOT NULL CONSTRAINT pk_tenancy_tenant_memberships PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL REFERENCES [tenancy].[tenants](id),
    user_id uniqueidentifier NOT NULL REFERENCES [identity].[users](id),
    status varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    display_name nvarchar(100) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    created_at datetimeoffset(7) NOT NULL,
    CONSTRAINT uq_tenancy_membership_user UNIQUE(tenant_id,user_id),
    CONSTRAINT uq_tenancy_membership_tenant_id UNIQUE(tenant_id,id)
);
CREATE TABLE [tenancy].[tenant_roles] (
    id uniqueidentifier NOT NULL CONSTRAINT pk_tenancy_tenant_roles PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL REFERENCES [tenancy].[tenants](id),
    code varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    name nvarchar(100) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    CONSTRAINT uq_tenancy_roles_code UNIQUE(tenant_id,code),
    CONSTRAINT uq_tenancy_roles_tenant_id UNIQUE(tenant_id,id)
);
CREATE TABLE [tenancy].[membership_roles] (
    tenant_id uniqueidentifier NOT NULL,
    membership_id uniqueidentifier NOT NULL,
    role_id uniqueidentifier NOT NULL,
    CONSTRAINT pk_tenancy_membership_roles PRIMARY KEY(tenant_id,membership_id,role_id),
    FOREIGN KEY(tenant_id,membership_id) REFERENCES [tenancy].[tenant_memberships](tenant_id,id),
    FOREIGN KEY(tenant_id,role_id) REFERENCES [tenancy].[tenant_roles](tenant_id,id)
);
CREATE TABLE [system].[permissions] (
    code varchar(128) COLLATE Latin1_General_100_BIN2 CONSTRAINT pk_system_permissions PRIMARY KEY,
    name nvarchar(100) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    scope varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL
);
CREATE TABLE [tenancy].[role_permissions] (
    tenant_id uniqueidentifier NOT NULL,
    role_id uniqueidentifier NOT NULL,
    permission_code varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL REFERENCES [system].[permissions](code),
    CONSTRAINT pk_tenancy_role_permissions PRIMARY KEY(tenant_id,role_id,permission_code),
    FOREIGN KEY(tenant_id,role_id) REFERENCES [tenancy].[tenant_roles](tenant_id,id)
);

CREATE TABLE [identity].[sessions] (
    id uniqueidentifier CONSTRAINT pk_identity_sessions PRIMARY KEY,
    user_id uniqueidentifier NOT NULL REFERENCES [identity].[users](id),
    tenant_id uniqueidentifier NULL,
    membership_id uniqueidentifier NULL,
    security_stamp nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_at datetimeoffset(7) NOT NULL,
    last_seen_at datetimeoffset(7) NOT NULL,
    expires_at datetimeoffset(7) NOT NULL,
    is_revoked bit NOT NULL DEFAULT 0,
    FOREIGN KEY(tenant_id,membership_id) REFERENCES [tenancy].[tenant_memberships](tenant_id,id)
);
CREATE INDEX ix_identity_sessions_user ON [identity].[sessions](user_id,is_revoked);

CREATE TABLE [projects].[projects] (
    id uniqueidentifier CONSTRAINT pk_projects_projects PRIMARY KEY NONCLUSTERED,
    storage_sequence bigint IDENTITY NOT NULL,
    tenant_id uniqueidentifier NOT NULL REFERENCES [tenancy].[tenants](id),
    name nvarchar(100) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    created_at datetimeoffset(7) NOT NULL,
    created_by uniqueidentifier NULL REFERENCES [identity].[users](id),
    updated_at datetimeoffset(7) NOT NULL,
    updated_by uniqueidentifier NULL REFERENCES [identity].[users](id),
    is_deleted bit NOT NULL DEFAULT 0,
    deleted_at datetimeoffset(7) NULL,
    deleted_by uniqueidentifier NULL REFERENCES [identity].[users](id),
    row_version rowversion NOT NULL,
    CONSTRAINT uq_projects_projects_tenant_id UNIQUE(tenant_id,id),
    CONSTRAINT ck_projects_projects_deleted CHECK((is_deleted=0 AND deleted_at IS NULL) OR (is_deleted=1 AND deleted_at IS NOT NULL))
);
CREATE UNIQUE CLUSTERED INDEX cx_projects_projects_sequence ON [projects].[projects](storage_sequence);
CREATE INDEX ix_projects_projects_list ON [projects].[projects](tenant_id,is_deleted,created_at DESC,id) INCLUDE(name,updated_at,row_version);

CREATE TABLE [audit].[events] (
    id bigint IDENTITY CONSTRAINT pk_audit_events PRIMARY KEY,
    event_id uniqueidentifier NOT NULL UNIQUE,
    tenant_id uniqueidentifier NULL,
    actor_id uniqueidentifier NULL,
    action varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    resource_id uniqueidentifier NULL,
    summary nvarchar(1000) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
    occurred_at datetimeoffset(7) NOT NULL,
    request_id varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    trace_id varchar(64) COLLATE Latin1_General_100_BIN2 NULL
);
CREATE INDEX ix_audit_events_tenant_time ON [audit].[events](tenant_id,occurred_at DESC,id);
CREATE TABLE [system].[idempotency_records] (
    tenant_id uniqueidentifier NOT NULL,
    actor_id uniqueidentifier NOT NULL,
    operation varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    key_hash binary(32) NOT NULL,
    request_hash binary(32) NOT NULL,
    status_code int NOT NULL,
    response_json nvarchar(max) COLLATE Latin1_General_100_CI_AS_SC NULL,
    location nvarchar(500) COLLATE Latin1_General_100_BIN2 NULL,
    etag varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    expires_at datetimeoffset(7) NOT NULL,
    CONSTRAINT pk_system_idempotency PRIMARY KEY(tenant_id,actor_id,operation,key_hash)
);
