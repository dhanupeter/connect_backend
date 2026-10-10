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

-- 6. Seed default conversations if empty
IF NOT EXISTS (SELECT TOP 1 1 FROM chat.Conversations)
BEGIN
    INSERT INTO chat.Conversations (ConversationId, IsGroup, Title, Description, AvatarUrl, ScopeRadiusKm, Category, CreatedAt)
    VALUES 
    ('grp_nh544', 1, N'⚡ NH-544 Express EV Convoy', N'Coimbatore - Tiruppur - Salem EV Commuters & Fast Charger availability alerts', 'https://images.unsplash.com/photo-1558441719-7d0840db04b9?auto=format&fit=crop&w=150&q=80', 50, 'Convoy', SYSUTCDATETIME()),
    ('grp_sos_rescue', 1, N'🔋 24/7 Battery SOS & Tow Network', N'100km radius emergency backup: portable 15A chargers, jump-start & recovery team', 'https://images.unsplash.com/photo-1617788138017-80ad40651399?auto=format&fit=crop&w=150&q=80', 100, 'SOS', SYSUTCDATETIME()),
    ('grp_weekend_riders', 1, N'🌿 Western Ghats EV Roadtrippers', N'Weekend hill climbs to Ooty, Valparai & Kodaikanal. Regen tips & hill chargers.', 'https://images.unsplash.com/photo-1469854523086-cc02fe5d8800?auto=format&fit=crop&w=150&q=80', 100, 'Roadtrip', SYSUTCDATETIME()),
    ('chat_user_rajesh', 0, N'Rajesh Kannan', N'Tata Tiago EV • TN 38 BK 4210', 'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?auto=format&fit=crop&w=150&q=80', 100, 'Direct', SYSUTCDATETIME()),
    ('chat_user_sarah', 0, N'Dr. Sarah Thomas', N'MG ZS EV • TN 37 EV 1990', 'https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?auto=format&fit=crop&w=150&q=80', 100, 'Direct', SYSUTCDATETIME());

    -- Seed initial messages
    INSERT INTO chat.Messages (MessageId, ConversationId, SenderUserId, SenderName, SenderAvatar, MessageText, MessageType, Status, CreatedAt)
    VALUES
    ('m1', 'grp_nh544', 2, N'Karthik EV', 'https://images.unsplash.com/photo-1519085360753-af0119f7cbe7?auto=format&fit=crop&w=150&q=80', N'Heavy traffic near Neelambur toll, watch your battery range if AC is running on full blast!', 'text', 'read', DATEADD(MINUTE, -45, SYSUTCDATETIME())),
    ('m2', 'grp_nh544', 3, N'Vikramaditya', 'https://images.unsplash.com/photo-1500648767791-00dcc994a43e?auto=format&fit=crop&w=150&q=80', N'⚡ Station Alert: Zeon 60kW Avinashi Hub has 1 CCS2 gun free right now!', 'station', 'read', DATEADD(MINUTE, -20, SYSUTCDATETIME())),
    ('m10', 'grp_sos_rescue', 99, N'Connect SOS Bot', 'https://images.unsplash.com/photo-1617788138017-80ad40651399?auto=format&fit=crop&w=150&q=80', N'🚨 100 KM Radius Emergency Support active. 14 fast-responders online within Coimbatore & surrounding highways.', 'sos', 'read', DATEADD(HOUR, -2, SYSUTCDATETIME())),
    ('m20', 'chat_user_rajesh', 1, N'Rajesh Kannan', 'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?auto=format&fit=crop&w=150&q=80', N'Hi Dhanu! Noticed you drive a Tata Nexon EV in Coimbatore area.', 'text', 'read', DATEADD(HOUR, -1, SYSUTCDATETIME())),
    ('m21', 'chat_user_rajesh', 4, N'Dhanu Peter', 'https://lh3.googleusercontent.com/a/default-user', N'Yes! TN 38 EV 8903. Battery is at 84% currently.', 'text', 'read', DATEADD(MINUTE, -30, SYSUTCDATETIME())),
    ('m22', 'chat_user_rajesh', 1, N'Rajesh Kannan', 'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?auto=format&fit=crop&w=150&q=80', N'Hey Dhanu, are you charging at the Zeon hub tonight?', 'text', 'read', DATEADD(MINUTE, -10, SYSUTCDATETIME()));
END
GO
