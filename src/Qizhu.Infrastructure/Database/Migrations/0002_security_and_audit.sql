-- 已执行的 0001 不再修改；新增安全计量与完整审计字段。
CREATE TABLE [identity].[login_windows] (
    kind varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    key_hash binary(32) NOT NULL,
    window_start datetimeoffset(7) NOT NULL,
    request_count int NOT NULL,
    CONSTRAINT pk_identity_login_windows PRIMARY KEY(kind,key_hash,window_start)
);
ALTER TABLE [audit].[events] ADD
    actor_type varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT 'user',
    resource_type varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT 'projects',
    result varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT 'success';
