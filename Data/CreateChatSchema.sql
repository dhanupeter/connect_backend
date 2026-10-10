USE ConnectEV;
GO

-- 1. Ensure chat schema exists
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'chat')
BEGIN
    EXEC('CREATE SCHEMA chat');
END
GO

-- 2. Add location columns to identity.Users if not existing
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('identity.Users') AND name = 'Latitude')
BEGIN
    ALTER TABLE identity.Users ADD Latitude DECIMAL(9,6) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('identity.Users') AND name = 'Longitude')
BEGIN
    ALTER TABLE identity.Users ADD Longitude DECIMAL(9,6) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('identity.Users') AND name = 'LastActiveAt')
BEGIN
    ALTER TABLE identity.Users ADD LastActiveAt DATETIME2 NULL;
END
GO

-- 3. Create chat.Conversations
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Conversations' AND schema_id = SCHEMA_ID('chat'))
BEGIN
    CREATE TABLE chat.Conversations (
        ConversationId VARCHAR(64) NOT NULL PRIMARY KEY,
        IsGroup BIT NOT NULL DEFAULT 0,
        Title NVARCHAR(150) NOT NULL,
        Description NVARCHAR(500) NULL,
        AvatarUrl NVARCHAR(500) NULL,
        ScopeRadiusKm INT NOT NULL DEFAULT 100,
        Category NVARCHAR(50) NULL DEFAULT 'General',
        CreatedByUserId BIGINT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 4. Create chat.ConversationParticipants
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ConversationParticipants' AND schema_id = SCHEMA_ID('chat'))
BEGIN
    CREATE TABLE chat.ConversationParticipants (
        ConversationId VARCHAR(64) NOT NULL,
        UserId BIGINT NOT NULL,
        JoinedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        LastReadMessageId VARCHAR(64) NULL,
        LastReadAt DATETIME2 NULL,
        IsAdmin BIT NOT NULL DEFAULT 0,
        CONSTRAINT PK_ChatParticipants PRIMARY KEY (ConversationId, UserId)
    );
END
GO

-- 5. Create chat.Messages
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Messages' AND schema_id = SCHEMA_ID('chat'))
BEGIN
    CREATE TABLE chat.Messages (
        MessageId VARCHAR(64) NOT NULL PRIMARY KEY,
        ConversationId VARCHAR(64) NOT NULL,
        SenderUserId BIGINT NOT NULL,
        SenderName NVARCHAR(150) NOT NULL,
        SenderAvatar NVARCHAR(500) NULL,
        MessageText NVARCHAR(MAX) NOT NULL,
        MessageType NVARCHAR(50) NOT NULL DEFAULT 'text', -- 'text', 'image', 'file', 'location', 'station', 'sos'
        AttachmentUrl NVARCHAR(500) NULL,
        FileName NVARCHAR(250) NULL,
        FileSize BIGINT NULL,
        MetadataJson NVARCHAR(MAX) NULL,
        Status NVARCHAR(20) NOT NULL DEFAULT 'delivered', -- 'sent', 'delivered', 'read'
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

