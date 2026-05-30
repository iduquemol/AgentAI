-- Idempotente: IF NOT EXISTS en cada insercion.

IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'admin@convagent.local')
BEGIN
    INSERT INTO Users (Email, PasswordHash, DisplayName, Role, IsActive)
    VALUES (
        'admin@convagent.local',
        '$2a$12$DLih1AyQwzftHofNHUhq7uyJgh3C0Q/SM7U8p0nIYF3OfPadHFWje',
        'Administrador',
        'Admin',
        1
    );
END;

IF NOT EXISTS (SELECT 1 FROM Agents WHERE ContextRoute = '/chat/test')
BEGIN
    INSERT INTO Agents (Name, Description, ContextRoute, IsActive)
    VALUES (
        'Agente de Prueba',
        'Agente para verificar el funcionamiento del sistema durante desarrollo.',
        '/chat/test',
        1
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM   AgentPrompts ap
    JOIN   Agents a ON a.Id = ap.AgentId
    WHERE  a.ContextRoute = '/chat/test'
    AND    ap.Version = 1
)
BEGIN
    INSERT INTO AgentPrompts (AgentId, Version, SystemPrompt, IsActive, CreatedBy)
    SELECT
        a.Id,
        1,
        'Eres un asistente de prueba. Responde de forma concisa y util.',
        1,
        u.Id
    FROM  Agents a
    CROSS JOIN Users u
    WHERE a.ContextRoute = '/chat/test'
    AND   u.Email        = 'admin@convagent.local';
END;
