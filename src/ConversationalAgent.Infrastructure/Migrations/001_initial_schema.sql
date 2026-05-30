CREATE TABLE Users (
    Id           INT IDENTITY PRIMARY KEY,
    Email        NVARCHAR(256) NOT NULL,
    PasswordHash NVARCHAR(512) NOT NULL,
    DisplayName  NVARCHAR(200) NOT NULL,
    Role         NVARCHAR(50)  NOT NULL DEFAULT 'User',
    CreatedAt    DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    IsActive     BIT           NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Users_Email UNIQUE (Email)
);

CREATE TABLE Agents (
    Id           INT IDENTITY PRIMARY KEY,
    Name         NVARCHAR(200)  NOT NULL,
    Description  NVARCHAR(1000) NULL,
    ContextRoute NVARCHAR(500)  NOT NULL,
    IsActive     BIT            NOT NULL DEFAULT 1,
    CreatedAt    DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt    DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT UQ_Agents_ContextRoute UNIQUE (ContextRoute)
);

CREATE TABLE AgentPrompts (
    Id           INT IDENTITY PRIMARY KEY,
    AgentId      INT           NOT NULL REFERENCES Agents(Id),
    Version      INT           NOT NULL,
    SystemPrompt NVARCHAR(MAX) NOT NULL,
    IsActive     BIT           NOT NULL DEFAULT 0,
    CreatedBy    INT           NOT NULL REFERENCES Users(Id),
    CreatedAt    DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT UQ_AgentPrompt_Version UNIQUE (AgentId, Version)
);

-- Garantiza que solo exista un prompt activo por agente en todo momento
CREATE UNIQUE INDEX IX_AgentPrompts_AgentId_Active
    ON AgentPrompts(AgentId)
    WHERE IsActive = 1;

CREATE TABLE AgentTools (
    Id            INT IDENTITY PRIMARY KEY,
    AgentId       INT           NOT NULL REFERENCES Agents(Id),
    ToolClassName NVARCHAR(300) NOT NULL,
    IsEnabled     BIT           NOT NULL DEFAULT 1,
    CreatedAt     DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT UQ_AgentTools_AgentTool UNIQUE (AgentId, ToolClassName)
);

CREATE TABLE ConversationSessions (
    Id           UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    UserId       INT              NOT NULL REFERENCES Users(Id),
    AgentId      INT              NOT NULL REFERENCES Agents(Id),
    StartedAt    DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    LastActivity DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    IsActive     BIT              NOT NULL DEFAULT 1
);

CREATE INDEX IX_Sessions_UserId
    ON ConversationSessions(UserId, IsActive);

CREATE TABLE MessageAuditLog (
    Id           BIGINT IDENTITY PRIMARY KEY,
    SessionId    UNIQUEIDENTIFIER NOT NULL REFERENCES ConversationSessions(Id),
    Role         NVARCHAR(20)     NOT NULL,
    ContentHash  NVARCHAR(64)     NOT NULL,
    TokensUsed   INT              NULL,
    ToolsInvoked NVARCHAR(MAX)    NULL,
    CreatedAt    DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

CREATE INDEX IX_AuditLog_SessionId
    ON MessageAuditLog(SessionId);

CREATE TABLE RefreshTokens (
    Id        INT IDENTITY PRIMARY KEY,
    UserId    INT           NOT NULL REFERENCES Users(Id),
    Token     NVARCHAR(512) NOT NULL,
    ExpiresAt DATETIME2     NOT NULL,
    CreatedAt DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    IsRevoked BIT           NOT NULL DEFAULT 0,
    CONSTRAINT UQ_RefreshTokens_Token UNIQUE (Token)
);
