-- infra/sqlserver/create-database.sql
-- Cria o banco e o schema inicial do PulseSystem.
-- As migrations do EF Core vão evoluir este schema — este script
-- garante apenas que o banco exista quando a aplicação iniciar.

-- ============================================================
-- 1. Banco de dados
-- ============================================================
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'PulseSystem')
BEGIN
    CREATE DATABASE PulseSystem
        COLLATE Latin1_General_100_CI_AS_SC_UTF8;
    PRINT 'Database PulseSystem criado.';
END
ELSE
BEGIN
    PRINT 'Database PulseSystem já existe — pulando criação.';
END
GO

USE PulseSystem;
GO

-- ============================================================
-- 2. Schema da aplicação
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'pulse')
BEGIN
    EXEC('CREATE SCHEMA pulse');
    PRINT 'Schema pulse criado.';
END
GO

-- ============================================================
-- 3. Tabela principal de pulseicações
-- Será gerenciada pelo EF Core Migrations em runtime,
-- mas criamos aqui para ter o banco utilizável imediatamente
-- sem depender do migration no startup.
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'pulse' AND t.name = 'Notifications'
)
BEGIN
    CREATE TABLE pulse.Notifications (
        Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWSEQUENTIALID(),
        Recipient       NVARCHAR(256)       NOT NULL,
        Subject         NVARCHAR(512)       NOT NULL,
        Body            NVARCHAR(MAX)       NOT NULL,
        Type            TINYINT             NOT NULL,   -- 0=Email, 1=Push, 2=Sms
        Status          TINYINT             NOT NULL DEFAULT 0, -- 0=Pending, 1=Sent, 2=Failed
        CreatedAt       DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
        ProcessedAt     DATETIME2           NULL,
        ErrorMessage    NVARCHAR(1024)      NULL,
        IsDispatched    BIT                 NOT NULL DEFAULT 0, -- Outbox Pattern (Projeto 03)

        CONSTRAINT PK_Notifications PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_Notifications_Type   CHECK (Type   IN (0, 1, 2)),
        CONSTRAINT CK_Notifications_Status CHECK (Status IN (0, 1, 2))
    );

    -- Índice para queries por status (worker consulta Pending)
    CREATE NONCLUSTERED INDEX IX_Notifications_Status_CreatedAt
        ON pulse.Notifications (Status, CreatedAt DESC);

    -- Índice para o Outbox Pattern (Projeto 03)
    CREATE NONCLUSTERED INDEX IX_Notifications_IsDispatched
        ON pulse.Notifications (IsDispatched)
        WHERE IsDispatched = 0;

    PRINT 'Tabela pulse.Notifications criada.';
END
ELSE
BEGIN
    PRINT 'Tabela pulse.Notifications já existe — pulando criação.';
END
GO

-- ============================================================
-- 4. Tabela Outbox — preparada para o Projeto 03
-- Fica aqui como placeholder; o EF Core vai gerenciar via migration.
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'pulse' AND t.name = 'OutboxEntries'
)
BEGIN
    CREATE TABLE pulse.OutboxEntries (
        Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWSEQUENTIALID(),
        MessageType     NVARCHAR(256)       NOT NULL,
        Payload         NVARCHAR(MAX)       NOT NULL,
        RoutingKey      NVARCHAR(256)       NOT NULL,
        CreatedAt       DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
        ProcessedAt     DATETIME2           NULL,
        ErrorMessage    NVARCHAR(1024)      NULL,
        RetryCount      SMALLINT            NOT NULL DEFAULT 0,

        CONSTRAINT PK_OutboxEntries PRIMARY KEY CLUSTERED (Id)
    );

    -- Índice para o job de dispatch (lê entradas não processadas)
    CREATE NONCLUSTERED INDEX IX_OutboxEntries_ProcessedAt
        ON pulse.OutboxEntries (ProcessedAt)
        WHERE ProcessedAt IS NULL;

    PRINT 'Tabela pulse.OutboxEntries criada (placeholder Projeto 03).';
END
GO

-- ============================================================
-- 5. Verificação final
-- ============================================================
SELECT
    s.name      AS [Schema],
    t.name      AS [Tabela],
    p.rows      AS [Linhas]
FROM sys.tables t
JOIN sys.schemas s      ON t.schema_id = s.schema_id
JOIN sys.partitions p   ON t.object_id = p.object_id AND p.index_id IN (0,1)
WHERE s.name = 'pulse'
ORDER BY t.name;
GO

PRINT '✅ Script de inicialização concluído.';
GO
