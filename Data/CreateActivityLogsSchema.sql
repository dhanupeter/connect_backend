USE ConnectEV;
GO

-- Create ActivityLogs table for tracking all operations, warnings, errors, and exceptions
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ActivityLogs' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.ActivityLogs (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId NVARCHAR(100) NULL,
        Action NVARCHAR(100) NOT NULL,
        Category NVARCHAR(50) NOT NULL, -- Chat, Call, Auth, Permission, System
        Status NVARCHAR(20) NOT NULL,   -- Success, Warning, Error, Exception
        Message NVARCHAR(MAX) NOT NULL,
        ExceptionDetails NVARCHAR(MAX) NULL,
        CorrelationId NVARCHAR(100) NULL,
        CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE NONCLUSTERED INDEX IX_ActivityLogs_Status ON dbo.ActivityLogs(Status);
    CREATE NONCLUSTERED INDEX IX_ActivityLogs_Category ON dbo.ActivityLogs(Category);
    CREATE NONCLUSTERED INDEX IX_ActivityLogs_CreatedAtUtc ON dbo.ActivityLogs(CreatedAtUtc DESC);
    CREATE NONCLUSTERED INDEX IX_ActivityLogs_UserId ON dbo.ActivityLogs(UserId);
    CREATE NONCLUSTERED INDEX IX_ActivityLogs_CorrelationId ON dbo.ActivityLogs(CorrelationId);
END
GO
