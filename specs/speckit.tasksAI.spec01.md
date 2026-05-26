# SPEC-01 · Tasks AI — Fundación: Solución, Migraciones y Configuración
> Proyecto: `ConversationalAgent` (solución .NET 9 + React/Vite)
> Regla: **Tests primero, implementación después** (constitution §16)
> Cobertura mínima: **80%** en Domain y Application · Integración con Testcontainers en Infrastructure

---

## Bloque A — Estructura de solución .NET + carpetas base (RF-01, RF-02)

### TASK-01-A1 · Crear la solución y los 8 proyectos

- **Acción:** Scaffolding manual — no hay tests previos para estructura de archivos.
- **Comandos:**

```powershell
dotnet new sln -n ConversationalAgent

# Proyectos en src/
dotnet new classlib -n ConversationalAgent.Domain         -o src/ConversationalAgent.Domain         -f net9.0
dotnet new classlib -n ConversationalAgent.Application    -o src/ConversationalAgent.Application    -f net9.0
dotnet new classlib -n ConversationalAgent.Infrastructure -o src/ConversationalAgent.Infrastructure -f net9.0
dotnet new web      -n ConversationalAgent.Api            -o src/ConversationalAgent.Api            -f net9.0

# Proyectos en tests/
dotnet new xunit -n ConversationalAgent.Domain.Tests         -o tests/ConversationalAgent.Domain.Tests
dotnet new xunit -n ConversationalAgent.Application.Tests    -o tests/ConversationalAgent.Application.Tests
dotnet new xunit -n ConversationalAgent.Infrastructure.Tests -o tests/ConversationalAgent.Infrastructure.Tests
dotnet new xunit -n ConversationalAgent.Api.Tests            -o tests/ConversationalAgent.Api.Tests

# Agregar todos los proyectos a la solución
dotnet sln add src/ConversationalAgent.Domain/ConversationalAgent.Domain.csproj
dotnet sln add src/ConversationalAgent.Application/ConversationalAgent.Application.csproj
dotnet sln add src/ConversationalAgent.Infrastructure/ConversationalAgent.Infrastructure.csproj
dotnet sln add src/ConversationalAgent.Api/ConversationalAgent.Api.csproj
dotnet sln add tests/ConversationalAgent.Domain.Tests/ConversationalAgent.Domain.Tests.csproj
dotnet sln add tests/ConversationalAgent.Application.Tests/ConversationalAgent.Application.Tests.csproj
dotnet sln add tests/ConversationalAgent.Infrastructure.Tests/ConversationalAgent.Infrastructure.Tests.csproj
dotnet sln add tests/ConversationalAgent.Api.Tests/ConversationalAgent.Api.Tests.csproj
```

- **Referencias entre proyectos** (dirección constitution §4.1):

```powershell
dotnet add src/ConversationalAgent.Application    reference src/ConversationalAgent.Domain
dotnet add src/ConversationalAgent.Infrastructure reference src/ConversationalAgent.Domain
dotnet add src/ConversationalAgent.Api            reference src/ConversationalAgent.Application
dotnet add src/ConversationalAgent.Api            reference src/ConversationalAgent.Infrastructure

dotnet add tests/ConversationalAgent.Domain.Tests         reference src/ConversationalAgent.Domain
dotnet add tests/ConversationalAgent.Application.Tests    reference src/ConversationalAgent.Application
dotnet add tests/ConversationalAgent.Infrastructure.Tests reference src/ConversationalAgent.Infrastructure
dotnet add tests/ConversationalAgent.Api.Tests            reference src/ConversationalAgent.Api
```

- **Paquetes NuGet obligatorios:**

```powershell
# Infrastructure
dotnet add src/ConversationalAgent.Infrastructure package Dapper               --version "2.*"
dotnet add src/ConversationalAgent.Infrastructure package Microsoft.Data.SqlClient --version "5.*"
dotnet add src/ConversationalAgent.Infrastructure package dbup-sqlserver        --version "5.*"
dotnet add src/ConversationalAgent.Infrastructure package BCrypt.Net-Next       --version "4.*"

# Api
dotnet add src/ConversationalAgent.Api package Microsoft.AspNetCore.OpenApi  --version "9.*"
dotnet add src/ConversationalAgent.Api package Scalar.AspNetCore             --version "1.*"

# Todos los proyectos de test
foreach ($proj in @(
    "tests/ConversationalAgent.Domain.Tests",
    "tests/ConversationalAgent.Application.Tests",
    "tests/ConversationalAgent.Infrastructure.Tests",
    "tests/ConversationalAgent.Api.Tests"
)) {
    dotnet add $proj package xunit                     --version "2.*"
    dotnet add $proj package xunit.runner.visualstudio --version "2.*"
    dotnet add $proj package FluentAssertions          --version "6.*"
    dotnet add $proj package Moq                       --version "4.*"
}

# Adicionales por proyecto de test
dotnet add tests/ConversationalAgent.Infrastructure.Tests package Testcontainers.MsSql         --version "3.*"
dotnet add tests/ConversationalAgent.Api.Tests            package Microsoft.AspNetCore.Mvc.Testing
```

- **Completado cuando:** `dotnet build` retorna `0 Error(s)  0 Warning(s)`.

---

### TASK-01-A2 · Crear carpetas base con `.gitkeep` (RF-02)

Estas carpetas deben existir desde SPEC-01 para que los specs siguientes las usen sin crearlas.

```powershell
# Domain
$domainBase = "src/ConversationalAgent.Domain"
foreach ($dir in @("Entities","Interfaces","LLM","Models","SignalR","Exceptions")) {
    New-Item -ItemType File -Force "$domainBase/$dir/.gitkeep"
}

# Application
$appBase = "src/ConversationalAgent.Application"
foreach ($dir in @("UseCases","Services")) {
    New-Item -ItemType File -Force "$appBase/$dir/.gitkeep"
}

# Infrastructure
$infraBase = "src/ConversationalAgent.Infrastructure"
foreach ($dir in @("Data","Cache","Prompts","Guardrails","Sql","Migrations","Auth","LLM","Tools")) {
    New-Item -ItemType File -Force "$infraBase/$dir/.gitkeep"
}

# Api
$apiBase = "src/ConversationalAgent.Api"
foreach ($dir in @("Endpoints","Hubs")) {
    New-Item -ItemType File -Force "$apiBase/$dir/.gitkeep"
}
```

- **Tests triviales (RF-11):** Crear un placeholder en cada proyecto de test para que `dotnet test` los descubra:

```csharp
// tests/ConversationalAgent.Domain.Tests/PlaceholderTest.cs
namespace ConversationalAgent.Domain.Tests;
public class PlaceholderTest
{
    [Fact] public void Domain_TestInfrastructure_Works() => Assert.True(true);
}
```

Replicar para `Application.Tests`, `Infrastructure.Tests` y `Api.Tests` con namespace y nombre de clase correspondiente.

- **Completado cuando:** `dotnet test` pasa los 4 placeholders + las carpetas base existen con `.gitkeep` (CA-16).

---

## Bloque B — IDbConnectionFactory (RF-06)

> Este bloque va antes de las migraciones porque `Program.cs` lo usa para DbUp.

### TASK-01-B1 · Test: `SqlConnectionFactory` valida configuración

- **Archivo:** `tests/ConversationalAgent.Infrastructure.Tests/Data/SqlConnectionFactoryTests.cs`
- **Casos:**
  - [ ] Constructor con `IConfiguration` que tiene `ConnectionStrings:DefaultConnection` → no lanza excepción.
  - [ ] Constructor con `IConfiguration` sin `ConnectionStrings:DefaultConnection` → lanza `InvalidOperationException` con mensaje descriptivo.
  - [ ] El mensaje de la excepción menciona `"ConnectionStrings:DefaultConnection"`.
- **Nota:** Usar Moq para `IConfiguration` — sin BD real en este test.
- **Completado cuando:** test compila y falla (rojo).

### TASK-01-B2 · Implementar `IDbConnectionFactory` y `SqlConnectionFactory`

```csharp
// src/ConversationalAgent.Infrastructure/Data/IDbConnectionFactory.cs
using System.Data;
namespace ConversationalAgent.Infrastructure.Data;

public interface IDbConnectionFactory
{
    /// Crea y abre una nueva conexión a SQL Server.
    /// El llamador es responsable de hacer Dispose.
    IDbConnection Create();
}
```

```csharp
// src/ConversationalAgent.Infrastructure/Data/SqlConnectionFactory.cs
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
namespace ConversationalAgent.Infrastructure.Data;

public sealed class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection no está configurada. " +
                "Verifica appsettings.Development.json o las variables de entorno.");
    }

    public IDbConnection Create()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
```

- **Completado cuando:** tests de TASK-01-B1 pasan (verde) — CA-10.

---

## Bloque C — Migraciones DbUp (RF-03, RF-04, RF-05)

### TASK-01-C1 · Test de integración: migraciones crean el esquema completo

- **Archivo:** `tests/ConversationalAgent.Infrastructure.Tests/Migrations/MigrationTests.cs`
- **Tecnología:** Testcontainers para SQL Server real — nunca mocks de BD (constitution §16.3).
- **Casos:**
  - [ ] Ejecutar DbUp contra BD vacía → no lanza excepción.
  - [ ] Tras migrar, `INFORMATION_SCHEMA.TABLES` contiene estas 7 tablas: `Users`, `Agents`, `AgentPrompts`, `AgentTools`, `ConversationSessions`, `MessageAuditLog`, `RefreshTokens`.
  - [ ] Constraint `UQ_AgentPrompt_Version` existe en `INFORMATION_SCHEMA.TABLE_CONSTRAINTS`.
  - [ ] El índice único filtrado impide insertar un segundo `AgentPrompt` con `IsActive = 1` para el mismo `AgentId` → lanza excepción de SQL (CA-06).
  - [ ] Tras seed: `SELECT COUNT(*) FROM Users WHERE Email = 'admin@convagent.local'` = 1 (CA-07).
  - [ ] Tras seed: `SELECT COUNT(*) FROM Agents WHERE ContextRoute = '/chat/test'` = 1 (CA-07).
  - [ ] Tras seed: `SELECT COUNT(*) FROM AgentPrompts WHERE IsActive = 1` = 1 (CA-07).
  - [ ] Ejecutar DbUp dos veces seguidas → no lanza excepción, datos no se duplican (CA-08).
- **Completado cuando:** test compila y falla (rojo).

### TASK-01-C2 · Crear scripts SQL y configurar como `EmbeddedResource`

**Archivo:** `src/ConversationalAgent.Infrastructure/Migrations/001_initial_schema.sql`

```sql
-- 001_initial_schema.sql

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
```

**Archivo:** `src/ConversationalAgent.Infrastructure/Migrations/002_seed_admin.sql`

```sql
-- 002_seed_admin.sql
-- Idempotente: IF NOT EXISTS en cada inserción.
-- ─────────────────────────────────────────────────────────────────────────
-- IMPORTANTE: reemplazar el hash placeholder antes del primer uso.
-- Generar el hash real con:
--   Console.WriteLine(BCrypt.Net.BCrypt.HashPassword("Admin1234!", 12));
-- y pegar el resultado en lugar de '$2a$12$PLACEHOLDER...'
-- Este hash es solo para desarrollo local — nunca usar en producción.
-- ─────────────────────────────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'admin@convagent.local')
BEGIN
    INSERT INTO Users (Email, PasswordHash, DisplayName, Role, IsActive)
    VALUES (
        'admin@convagent.local',
        '$2a$12$PLACEHOLDER_REEMPLAZAR_ANTES_DE_USAR',
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
        'Eres un asistente de prueba. Responde de forma concisa y útil.',
        1,
        u.Id
    FROM  Agents a
    CROSS JOIN Users u
    WHERE a.ContextRoute = '/chat/test'
    AND   u.Email        = 'admin@convagent.local';
END;
```

**Agregar al `.csproj` de Infrastructure** para que los SQL se embeben en el ensamblado:

```xml
<ItemGroup>
  <EmbeddedResource Include="Migrations\*.sql" />
</ItemGroup>
```

Eliminar el `.gitkeep` de `Migrations/` al agregar los scripts reales.

- **Completado cuando:** tests de TASK-01-C1 pasan (verde) — CA-05, CA-06, CA-07, CA-08.

---

## Bloque D — `Program.cs` + Health Check (RF-03, RF-10)

### TASK-01-D1 · Test: `GET /health` responde correctamente

- **Archivo:** `tests/ConversationalAgent.Api.Tests/HealthCheckTests.cs`
- **Tecnología:** `WebApplicationFactory<Program>` + cliente HTTP real.
- **Casos:**
  - [ ] `GET /health` → `200 OK`.
  - [ ] Body JSON contiene `"status"` con valor `"Healthy"`.
  - [ ] `GET /health` sin `Authorization` header → igual `200 OK` (no requiere JWT).
- **Nota:** Para que `WebApplicationFactory` compile, `Program.cs` debe terminar con `public partial class Program { }`.
- **Completado cuando:** test compila y falla (rojo).

### TASK-01-D2 · Implementar `Program.cs` mínimo con DbUp, DI y health check

```csharp
// src/ConversationalAgent.Api/Program.cs
using ConversationalAgent.Infrastructure.Data;
using DbUp;

var builder = WebApplication.CreateBuilder(args);

// ── Servicios base ────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

// ── (SPEC-02 agregará JWT y Auth aquí) ───────────────────────────────────
// ── (SPEC-03 agregará Redis / IAgentCacheService aquí) ───────────────────
// ── (SPEC-05 agregará SignalR aquí) ──────────────────────────────────────

var app = builder.Build();

// ── Migraciones DbUp — antes de mapear endpoints ─────────────────────────
var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection no está configurada.");

var upgrader = DeployChanges.To
    .SqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(
        typeof(SqlConnectionFactory).Assembly,
        script => script.Contains("Migrations"))
    .WithTransaction()
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful)
{
    Console.Error.WriteLine($"Migración fallida: {result.Error}");
    Environment.Exit(1);
}

// ── Endpoints ─────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");

// ── (SPEC-02 agregará endpoints de auth aquí) ─────────────────────────────
// ── (SPEC-05 agregará MapHub<ConversationHub> aquí) ───────────────────────

app.Run();

// Necesario para WebApplicationFactory en Api.Tests
public partial class Program { }
```

- **Completado cuando:** tests de TASK-01-D1 pasan (verde) y `GET /health` responde `200 OK` al correr la API localmente — CA-09.

---

## Bloque E — Configuración y secretos (RF-07)

> Sin tests — verificación con `git status`.

### TASK-01-E1 · `appsettings.json`, `appsettings.Development.json` y `.gitignore`

**Archivo:** `src/ConversationalAgent.Api/appsettings.json` (commiteado — sin valores reales):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.AspNetCore.SignalR": "Debug"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": ""
  },
  "Jwt": {
    "SecretKey": "",
    "Issuer": "conversational-agent-api",
    "Audience": "conversational-agent-client",
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 7
  },
  "Claude": {
    "ApiKey": "",
    "Model": "claude-sonnet-4-20250514",
    "MaxTokens": 4096,
    "TimeoutSeconds": 60,
    "MaxRetries": 3,
    "MaxReActIterations": 10
  },
  "Redis": {
    "ConnectionString": "",
    "Enabled": true
  },
  "Guardrails": {
    "Enabled": false,
    "Rules": []
  }
}
```

**Archivo:** `src/ConversationalAgent.Api/appsettings.Development.json` (en `.gitignore` — valores locales reales):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=ConvAgent;User Id=sa;Password=TU_PASSWORD;TrustServerCertificate=True;"
  },
  "Jwt": {
    "SecretKey": "una-clave-de-desarrollo-de-al-menos-32-caracteres"
  },
  "Claude": {
    "ApiKey": "sk-ant-..."
  },
  "Redis": {
    "Enabled": false
  }
}
```

> Con `Redis__Enabled = false` en desarrollo local la API arranca sin Redis.
> Se usa `InMemoryAgentCacheService` en su lugar (se implementa en SPEC-03).

**Archivo:** `.gitignore` (raíz del repo):

```gitignore
# .NET
bin/
obj/
*.user
*.suo
.vs/
*.csproj.user

# Secretos locales — NUNCA commitear
.env
.env.local
.env.*.local
src/ConversationalAgent.Api/appsettings.Development.json

# Node / frontend
node_modules/
frontend/dist/
frontend/.vite/

# Docker local overrides
docker-compose.override.yml

# Herramientas del sistema
.DS_Store
Thumbs.db
```

- **Criterio:** `git status` no muestra `.env` ni `appsettings.Development.json` aunque existan localmente — CA-15.

---

## Bloque F — Docker Compose con Redis y Dockerfiles (RF-09)

> Verificación operacional — sin tests unitarios.

### TASK-01-F1 · Dockerfile del backend (multi-stage)

**Archivo:** `src/ConversationalAgent.Api/Dockerfile`

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["ConversationalAgent.Api/ConversationalAgent.Api.csproj",                         "ConversationalAgent.Api/"]
COPY ["ConversationalAgent.Application/ConversationalAgent.Application.csproj",         "ConversationalAgent.Application/"]
COPY ["ConversationalAgent.Domain/ConversationalAgent.Domain.csproj",                   "ConversationalAgent.Domain/"]
COPY ["ConversationalAgent.Infrastructure/ConversationalAgent.Infrastructure.csproj",   "ConversationalAgent.Infrastructure/"]

RUN dotnet restore "ConversationalAgent.Api/ConversationalAgent.Api.csproj"

COPY . .
RUN dotnet publish "ConversationalAgent.Api/ConversationalAgent.Api.csproj" \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ConversationalAgent.Api.dll"]
```

- **Criterio:** La imagen final no contiene el SDK de .NET.

### TASK-01-F2 · Dockerfile del frontend (multi-stage) + `nginx.conf`

**Archivo:** `frontend/Dockerfile`

```dockerfile
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
```

**Archivo:** `frontend/nginx.conf`

```nginx
server {
    listen 80;

    location / {
        root   /usr/share/nginx/html;
        index  index.html;
        try_files $uri $uri/ /index.html;
    }

    location /api/ {
        proxy_pass         http://api:8080/api/;
        proxy_http_version 1.1;
        proxy_set_header   Host      $host;
        proxy_set_header   X-Real-IP $remote_addr;
    }

    location /hubs/ {
        proxy_pass             http://api:8080/hubs/;
        proxy_http_version     1.1;
        proxy_set_header       Upgrade    $http_upgrade;
        proxy_set_header       Connection "upgrade";
        proxy_set_header       Host       $host;
        proxy_set_header       X-Real-IP  $remote_addr;
        proxy_read_timeout     3600s;
    }
}
```

### TASK-01-F3 · `docker-compose.yml` con Redis y `.env.example`

**Archivo:** `docker-compose.yml` (raíz del repo)

```yaml
services:
  api:
    build:
      context: ./src
      dockerfile: ConversationalAgent.Api/Dockerfile
    ports:
      - "5000:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__DefaultConnection=Server=host.docker.internal;Database=ConvAgent;User Id=${DB_USER};Password=${DB_PASSWORD};TrustServerCertificate=True;
      - Claude__ApiKey=${CLAUDE_API_KEY}
      - Jwt__SecretKey=${JWT_SECRET_KEY}
      - Redis__ConnectionString=${REDIS_CONNECTION_STRING}
      - Redis__Enabled=true
    extra_hosts:
      - "host.docker.internal:host-gateway"
    depends_on:
      - redis

  frontend:
    build:
      context: ./frontend
      dockerfile: Dockerfile
    ports:
      - "3000:80"
    depends_on:
      - api

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
```

**Archivo:** `.env.example` (raíz del repo)

```
# Conexión al SQL Server del host desde contenedores Docker
# La API corre en Docker — el SQL Server corre en la máquina host
DB_USER=sa
DB_PASSWORD=

# Clave secreta para firmar JWT (mínimo 32 caracteres)
# Generar con: openssl rand -base64 32
JWT_SECRET_KEY=

# API Key de Anthropic — obtener en console.anthropic.com
CLAUDE_API_KEY=sk-ant-...

# Redis (dentro de Docker apunta al servicio redis del compose)
REDIS_CONNECTION_STRING=redis:6379

# URLs del frontend (Vite — solo para desarrollo local fuera de Docker)
VITE_API_BASE_URL=http://localhost:5000
VITE_SIGNALR_HUB_URL=http://localhost:5000/hubs/conversation
```

- **Completado cuando:** `docker compose up --build` levanta `api`, `frontend` y `redis` sin errores — CA-13, CA-14.

---

## Bloque G — Scaffolding del Frontend React (RF-08)

> Verificación de compilación y visual — sin tests unitarios en este bloque.

### TASK-01-G1 · Inicializar proyecto Vite + React + TypeScript

```powershell
npm create vite@latest frontend -- --template react-ts
cd frontend
npm install
```

Verificar que `tsconfig.json` tiene `"strict": true` y `"noImplicitAny": true`.

### TASK-01-G2 · Instalar y configurar Tailwind CSS

```powershell
npm install -D tailwindcss postcss autoprefixer tailwindcss-animate
npx tailwindcss init -p
```

**Archivo:** `frontend/tailwind.config.ts`:

```typescript
import type { Config } from 'tailwindcss'

const config: Config = {
  darkMode: ['class'],
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        border:     'hsl(var(--border))',
        input:      'hsl(var(--input))',
        ring:       'hsl(var(--ring))',
        background: 'hsl(var(--background))',
        foreground: 'hsl(var(--foreground))',
        primary: {
          DEFAULT:    'hsl(var(--primary))',
          foreground: 'hsl(var(--primary-foreground))',
        },
        secondary: {
          DEFAULT:    'hsl(var(--secondary))',
          foreground: 'hsl(var(--secondary-foreground))',
        },
        muted: {
          DEFAULT:    'hsl(var(--muted))',
          foreground: 'hsl(var(--muted-foreground))',
        },
        accent: {
          DEFAULT:    'hsl(var(--accent))',
          foreground: 'hsl(var(--accent-foreground))',
        },
        destructive: {
          DEFAULT:    'hsl(var(--destructive))',
          foreground: 'hsl(var(--destructive-foreground))',
        },
      },
      borderRadius: {
        lg: 'var(--radius)',
        md: 'calc(var(--radius) - 2px)',
        sm: 'calc(var(--radius) - 4px)',
      },
    },
  },
  plugins: [require('tailwindcss-animate')],
}

export default config
```

**Archivo:** `frontend/src/index.css`:

```css
@tailwind base;
@tailwind components;
@tailwind utilities;

@layer base {
  :root {
    --background: 0 0% 100%;
    --foreground: 222.2 84% 4.9%;
    --border: 214.3 31.8% 91.4%;
    --input: 214.3 31.8% 91.4%;
    --ring: 222.2 84% 4.9%;
    --primary: 222.2 47.4% 11.2%;
    --primary-foreground: 210 40% 98%;
    --secondary: 210 40% 96.1%;
    --secondary-foreground: 222.2 47.4% 11.2%;
    --muted: 210 40% 96.1%;
    --muted-foreground: 215.4 16.3% 46.9%;
    --accent: 210 40% 96.1%;
    --accent-foreground: 222.2 47.4% 11.2%;
    --destructive: 0 84.2% 60.2%;
    --destructive-foreground: 210 40% 98%;
    --radius: 0.5rem;
  }
}
```

### TASK-01-G3 · Inicializar shadcn/ui y agregar componente Button

```powershell
npx shadcn@latest init
npx shadcn@latest add button
```

Verificar que `frontend/src/components/ui/button.tsx` existe — **no editarlo directamente** (constitution §13.8).

### TASK-01-G4 · Crear carpetas base con `.gitkeep` + tipos canónicos + utils

**Carpetas base del frontend (RF-08):**

```powershell
# Desde la raíz del repositorio
$frontendSrc = "frontend/src"
foreach ($dir in @(
    "api", "hub",
    "components/ui", "components/chat", "components/admin", "components/layout",
    "pages", "stores", "hooks", "types", "lib"
)) {
    New-Item -ItemType File -Force "$frontendSrc/$dir/.gitkeep"
}
```

> El `.gitkeep` de `components/ui/` se puede eliminar al instalar el componente Button en TASK-01-G3.

**Archivo:** `frontend/src/lib/utils.ts` (requerido por shadcn/ui):

```typescript
import { type ClassValue, clsx } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
```

```powershell
npm install clsx tailwind-merge
```

**Archivo:** `frontend/src/types/conversation.ts` (tipos canónicos — constitution §13.2):

```typescript
export interface ConversationMessage {
  role: 'user' | 'assistant';
  content: string;
  timestamp: string; // ISO 8601
}

export interface ConversationSession {
  sessionId: string;
  contextRoute: string;
  agentName: string;
  messages: ConversationMessage[];
  startedAt: string;
}
```

**Archivo:** `frontend/src/hub/hubEvents.ts` (constantes de eventos — constitution §13.4):

```typescript
export const HubEvents = {
  // Servidor → Cliente
  AgentThinking : 'AgentThinking',
  AgentStream   : 'AgentStream',
  AgentToolCall : 'AgentToolCall',
  AgentDone     : 'AgentDone',
  // Cliente → Servidor
  SendMessage   : 'SendMessage',
} as const;

export type AgentDoneStatus = 'completed' | 'error' | 'cancelled';

export interface AgentDonePayload {
  status: AgentDoneStatus;
  errorMessage: string | null;
}
```

### TASK-01-G5 · Configurar `vite.config.ts` con proxy y alias `@/`

```typescript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      '/hubs': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        ws: true,
      },
    },
  },
})
```

### TASK-01-G6 · Página placeholder en `App.tsx`

```tsx
// frontend/src/App.tsx
import { Button } from '@/components/ui/button'

export default function App() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-background">
      <div className="text-center space-y-4">
        <h1 className="text-2xl font-bold text-foreground">
          Agente Conversacional IA
        </h1>
        <Button>Comenzar</Button>
      </div>
    </main>
  )
}
```

- **Completado cuando:** `npm run build` completa con 0 errores de TypeScript y `npm run dev` muestra el texto `"Agente Conversacional IA"` con el `Button` visible — CA-11, CA-12.

---

## Bloque H — Verificación Final (RF-11)

### TASK-01-H1 · Build .NET sin errores ni warnings

```powershell
dotnet build ConversationalAgent.sln
```

- **Criterio:** `0 Error(s)  0 Warning(s)` — CA-01.

### TASK-01-H2 · Tests de Infrastructure pasan

```powershell
dotnet test tests/ConversationalAgent.Infrastructure.Tests/ConversationalAgent.Infrastructure.Tests.csproj --logger "console;verbosity=normal"
```

- **Criterio:** `SqlConnectionFactoryTests` y `MigrationTests` pasan — CA-05, CA-06, CA-07, CA-08, CA-10.

### TASK-01-H3 · Tests de Api pasan

```powershell
dotnet test tests/ConversationalAgent.Api.Tests/ConversationalAgent.Api.Tests.csproj --logger "console;verbosity=normal"
```

- **Criterio:** `HealthCheckTests` pasan — CA-09.

### TASK-01-H4 · Placeholders de Domain y Application pasan

```powershell
dotnet test tests/ConversationalAgent.Domain.Tests/ConversationalAgent.Domain.Tests.csproj
dotnet test tests/ConversationalAgent.Application.Tests/ConversationalAgent.Application.Tests.csproj
```

- **Criterio:** Los 2 placeholders se descubren y pasan.

### TASK-01-H5 · Suite completa de tests

```powershell
dotnet test ConversationalAgent.sln --logger "console;verbosity=normal"
```

- **Criterio:** Todos los proyectos de test reportan `Passed` — CA-02.

### TASK-01-H6 · Entorno Docker completo

```powershell
Copy-Item .env.example .env
# Editar .env: rellenar DB_USER, DB_PASSWORD, JWT_SECRET_KEY, CLAUDE_API_KEY
docker compose up --build
```

- **Verificaciones manuales:**

| CA | Check | Comando / Verificación |
|---|---|---|
| CA-03 | DbUp ejecuta los 2 scripts al primer arranque | Revisar logs del contenedor `api` |
| CA-04 | DbUp no re-ejecuta scripts en segunda arrancada | Reiniciar contenedor `api` y revisar logs |
| CA-05 | 7 tablas creadas con constraints e índices correctos | `SELECT TABLE_NAME FROM ConvAgent.INFORMATION_SCHEMA.TABLES` |
| CA-06 | Índice filtrado impide 2 prompts activos | Cubierto en TASK-01-C1 (test de integración) |
| CA-07 | Seed: admin + agente + prompt activo | `SELECT COUNT(*) FROM Users WHERE Email = 'admin@convagent.local'` → 1 |
| CA-08 | Seed idempotente | Reiniciar contenedor y verificar counts estables |
| CA-09 | `GET /health` → 200 OK | `curl http://localhost:5000/health` |
| CA-11 | `npm run build` sin errores | Verificación en Bloque G |
| CA-12 | `npm run dev` muestra placeholder | Abrir `http://localhost:5173` |
| CA-13 | `docker compose up` levanta api + frontend + redis | `docker compose ps` |
| CA-14 | Contenedor api conecta al SQL Server del host | Logs de DbUp — "Successfully upgraded" |
| CA-15 | `.env` y `appsettings.Development.json` no aparecen en git | `git status` |
| CA-16 | Carpetas base con `.gitkeep` existen en todos los proyectos | `git ls-files --others --exclude-standard` |

---

## Resumen de archivos a crear en SPEC-01

| # | Archivo | Bloque |
|---|---|---|
| 1 | `ConversationalAgent.sln` | A |
| 2 | `src/ConversationalAgent.Domain/ConversationalAgent.Domain.csproj` | A |
| 3 | `src/ConversationalAgent.Application/ConversationalAgent.Application.csproj` | A |
| 4 | `src/ConversationalAgent.Infrastructure/ConversationalAgent.Infrastructure.csproj` | A |
| 5 | `src/ConversationalAgent.Api/ConversationalAgent.Api.csproj` | A |
| 6 | `src/ConversationalAgent.Domain/{Entities,Interfaces,LLM,Models,SignalR,Exceptions}/.gitkeep` | A |
| 7 | `src/ConversationalAgent.Application/{UseCases,Services}/.gitkeep` | A |
| 8 | `src/ConversationalAgent.Infrastructure/{Data,Cache,Prompts,Guardrails,Sql,Migrations,Auth,LLM,Tools}/.gitkeep` | A |
| 9 | `src/ConversationalAgent.Api/{Endpoints,Hubs}/.gitkeep` | A |
| 10 | `tests/ConversationalAgent.*.Tests/PlaceholderTest.cs` (×4) | A |
| 11 | `src/ConversationalAgent.Infrastructure/Data/IDbConnectionFactory.cs` | B |
| 12 | `src/ConversationalAgent.Infrastructure/Data/SqlConnectionFactory.cs` | B |
| 13 | `tests/ConversationalAgent.Infrastructure.Tests/Data/SqlConnectionFactoryTests.cs` | B |
| 14 | `src/ConversationalAgent.Infrastructure/Migrations/001_initial_schema.sql` | C |
| 15 | `src/ConversationalAgent.Infrastructure/Migrations/002_seed_admin.sql` | C |
| 16 | `tests/ConversationalAgent.Infrastructure.Tests/Migrations/MigrationTests.cs` | C |
| 17 | `src/ConversationalAgent.Api/Program.cs` | D |
| 18 | `tests/ConversationalAgent.Api.Tests/HealthCheckTests.cs` | D |
| 19 | `src/ConversationalAgent.Api/appsettings.json` | E |
| 20 | `src/ConversationalAgent.Api/appsettings.Development.json` | E (gitignored) |
| 21 | `.gitignore` | E |
| 22 | `src/ConversationalAgent.Api/Dockerfile` | F |
| 23 | `frontend/Dockerfile` | F |
| 24 | `frontend/nginx.conf` | F |
| 25 | `docker-compose.yml` | F |
| 26 | `.env.example` | F |
| 27 | `frontend/vite.config.ts` | G |
| 28 | `frontend/tailwind.config.ts` | G |
| 29 | `frontend/tsconfig.json` | G |
| 30 | `frontend/src/index.css` | G |
| 31 | `frontend/src/App.tsx` | G |
| 32 | `frontend/src/lib/utils.ts` | G |
| 33 | `frontend/src/types/conversation.ts` | G |
| 34 | `frontend/src/hub/hubEvents.ts` | G |
| 35 | `frontend/src/components/ui/button.tsx` | G (shadcn CLI) |
| 36 | `frontend/src/{api,hub,components/*,pages,stores,hooks,types,lib}/.gitkeep` | G |

---

## Checklist SPEC-01

### Bloque A — Estructura de solución + carpetas base
- [x] TASK-01-A1 · Solución .NET con 4 proyectos src + 4 proyectos test, referencias y NuGet configurados
- [x] TASK-01-A2 · Carpetas base con `.gitkeep` en todos los proyectos + placeholders de test

### Bloque B — IDbConnectionFactory
- [ ] TASK-01-B1 · Test `SqlConnectionFactory` (rojo)
- [ ] TASK-01-B2 · Impl `IDbConnectionFactory` + `SqlConnectionFactory` (verde) — CA-10

### Bloque C — Migraciones DbUp
- [ ] TASK-01-C1 · Test de integración con Testcontainers (rojo)
- [ ] TASK-01-C2 · Scripts `001_initial_schema.sql` + `002_seed_admin.sql` como EmbeddedResource (verde) — CA-05, CA-06, CA-07, CA-08

### Bloque D — Program.cs + Health Check
- [ ] TASK-01-D1 · Test `GET /health` con `WebApplicationFactory` (rojo)
- [ ] TASK-01-D2 · `Program.cs` con DbUp + DI + `app.MapHealthChecks` + `partial class Program` (verde) — CA-09

### Bloque E — Configuración y secretos
- [ ] TASK-01-E1 · `appsettings.json` (con Redis + Guardrails) + `appsettings.Development.json` + `.gitignore` — CA-15

### Bloque F — Docker Compose con Redis
- [ ] TASK-01-F1 · Dockerfile del backend (multi-stage)
- [ ] TASK-01-F2 · Dockerfile del frontend (multi-stage) + `nginx.conf` con proxy WS para SignalR
- [ ] TASK-01-F3 · `docker-compose.yml` con servicios `api`, `frontend`, `redis` + `.env.example` — CA-13

### Bloque G — Frontend React
- [ ] TASK-01-G1 · Vite + React + TypeScript inicializado con strict mode
- [ ] TASK-01-G2 · Tailwind CSS configurado con variables CSS de shadcn/ui
- [ ] TASK-01-G3 · shadcn/ui inicializado + componente Button instalado
- [ ] TASK-01-G4 · Carpetas base con `.gitkeep` + `lib/utils.ts` + `types/conversation.ts` + `hub/hubEvents.ts`
- [ ] TASK-01-G5 · `vite.config.ts` con proxy `/api` y `/hubs` (WebSocket) + alias `@/`
- [ ] TASK-01-G6 · `App.tsx` con texto "Agente Conversacional IA" y Button visible — CA-11, CA-12

### Bloque H — Verificación Final
- [ ] TASK-01-H1 · `dotnet build` → 0 errores, 0 warnings — CA-01
- [ ] TASK-01-H2 · Tests de Infrastructure pasan — CA-05, CA-06, CA-07, CA-08, CA-10
- [ ] TASK-01-H3 · Tests de Api pasan — CA-09
- [ ] TASK-01-H4 · Placeholders de Domain y Application pasan
- [ ] TASK-01-H5 · `dotnet test` completo → todos pasan — CA-02
- [ ] TASK-01-H6 · `docker compose up --build` → api + frontend + redis operativos — CA-03, CA-04, CA-13, CA-14, CA-15, CA-16
