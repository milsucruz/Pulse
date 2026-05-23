-- infra/sqlserver/create-database.sql
-- Cria o banco e o schema inicial do PulseDB.
-- As migrations do EF Core vão evoluir este schema — este script
-- garante apenas que o banco exista quando a aplicação iniciar.

-- ============================================================
-- 1. Banco de dados
-- ============================================================
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'PulseDB')
BEGIN
    CREATE DATABASE PulseDB
        COLLATE Latin1_General_100_CI_AS_SC_UTF8;
    PRINT 'Database PulseDB criado.';
END
ELSE
BEGIN
    PRINT 'Database PulseDB já existe — pulando criação.';
END
GO

USE PulseDB;
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
-- 3. Verificação final
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
