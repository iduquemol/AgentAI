# SPEC-01 · Tasks — Fundación: Solución, Migraciones y Configuración
> Proyecto: `ConversationalAgent` (solución .NET 9 + React/Vite)
> Regla: **Tests primero, implementación después** (constitution §14)
> Cobertura mínima: **80%** en Domain y Application · Integración con Testcontainers en Infrastructure

---

## Bloque A — Estructura de solución .NET (RF-01)

### TASK-01-A1 · Crear la solución y los 4 proyectos

- **Acción:** Scaffolding manual (no hay tests previos para estructura de archivos).
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

# Agregar a la solución
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
# Application → Domain
dotnet add src/ConversationalAgent.Application reference src/ConversationalAgent.Domain

# Infrastructure → Domain
dotnet add src/ConversationalAgent.Infrastructure reference src/ConversationalAgent.Domain

# Api → Application + Infrastructure
dotnet add src/ConversationalAgent.Api reference src/ConversationalAgent.Application
dotnet add src/ConversationalAgent.Api reference src/ConversationalAgent.Infrastructure

# Tests referencian solo su proyecto objetivo
dotnet add tests/ConversationalAgent.Domain.Tests reference src/ConversationalAgent.Domain
dotnet add tests/ConversationalAgent.Application.Tests reference src/ConversationalAgent.Application
dotnet add tests/ConversationalAgent.Infrastructure.Tests reference src/ConversationalAgent.Infrastructure
dotnet add tests/ConversationalAgent.Api.Tests reference src/ConversationalAgent.Api
```

- **Paquetes NuGet obligatorios:**

```powershell
# Infrastructure
dotnet add src/ConversationalAgent.Infrastructure package Dapper
dotnet add src/ConversationalAgent.Infrastructure package Microsoft.Data.SqlClient
dotnet add src/ConversationalAgent.Infrastructure package dbup-sqlserver
dotnet add src/ConversationalAgent.Infrastructure package BCrypt.Net-Next

# Api
dotnet add src/ConversationalAgent.Api package Microsoft.AspNetCore.OpenApi
dotnet add src/ConversationalAgent.Api package Scalar.AspNetCore

# Tests de Infrastructure
dotnet add tests/ConversationalAgent.Infrastructure.Tests package xunit
dotnet add tests/ConversationalAgent.Infrastructure.Tests package xunit.runner.visualstudio
dotnet add tests/ConversationalAgent.Infrastructure.Tests package FluentAssertions
dotnet add tests/ConversationalAgent.Infrastructure.Tests package Testcontainers.MsSql
dotnet add tests/ConversationalAgent.Infrastructure.Tests package Moq

# Tests de Api
dotnet add tests/ConversationalAgent.Api.Tests package xunit
dotnet add tests/ConversationalAgent.Api.Tests package xunit.runner.visualstudio
dotnet add tests/ConversationalAgent.Api.Tests package FluentAssertions
dotnet add tests/ConversationalAgent.Api.Tests package Microsoft.AspNetCore.Mvc.Testing

# Tests triviales (Domain y Application)
dotnet add tests/ConversationalAgent.Domain.Tests package xunit
dotnet add tests/ConversationalAgent.Domain.Tests package xunit.runner.visualstudio
dotnet add tests/ConversationalAgent.Application.Tests package xunit
dotnet add tests/ConversationalAgent.Application.Tests package xunit.runner.visualstudio
```

- **Tests triviales (RF-09):** Crear un placeholder en cada proyecto de test para que `dotnet test` pueda descubrirlos:

```csharp
// tests/ConversationalAgent.Domain.Tests/PlaceholderTest.cs
namespace ConversationalAgent.Domain.Tests;

public class PlaceholderTest
{
    [Fact]
    public void Domain_TestInfrastructure_Works() => Assert.True(true);
}
```

Replicar para `Application.Tests`, `Infrastructure.Tests` y `Api.Tests` con el namespace y nombre de clase correspondiente.

- **Completado cuando:** `dotnet build` retorna `0 Error(s)  0 Warning(s)` y los 8 proyectos existen con las referencias correctas.

---

## Bloque B — IDbConnectionFactory (RF-05)

> Este bloque va antes de las migraciones porque Program.cs la usa para DbUp.

### TASK-01-B1 · Test: `SqlConnectionFactory` valida configuración

- **Archivo test:** `tests/ConversationalAgent.Infrastructure.Tests/Data/SqlConnectionFactoryTests.cs`
- **Casos:**
  - [ ] Constructor con `IConfiguration` que tiene `ConnectionStrings:DefaultConnection` → no lanza excepción.
  - [ ] Constructor con `IConfiguration` sin `ConnectionStrings:DefaultConnection` → lanza `InvalidOperationException` con mensaje descriptivo.
  - [ ] `Create()` retorna una instancia de `IDbConnection` no nula.
  - [ ] `Create()` retorna una **conexión abierta** (`State == ConnectionState.Open`).
  - [ ] `Create()` retorna una **nueva instancia** en cada llamada (no singleton de conexión).
- **Nota:** Usar Moq para `IConfiguration`. Los tests de `Create()` que abren conexión real requieren SQL Server — marcarlos con `[Trait("Category", "Integration")]` y usar `appsettings.Development.json` o una variable de entorno. El test de `InvalidOperationException` no requiere BD.
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
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection no está configurada.");
    }

    public IDbConnection Create()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
```

- **Completado cuando:** tests de TASK-01-B1 pasan (verde).

---

## Bloque C — Migraciones DbUp (RF-02, RF-03, RF-04)

### TASK-01-C1 · Test de integración: migraciones crean el esquema completo

- **Archivo test:** `tests/ConversationalAgent.Infrastructure.Tests/Migrations/MigrationTests.cs`
- **Tecnología:** Testcontainers para SQL Server real (never mocks de BD — constitution §14.3).
- **Casos:**
  - [ ] Ejecutar DbUp contra BD vacía → no lanza excepción.
  - [ ] Tras migrar, `INFORMATION_SCHEMA.TABLES` contiene exactamente estas 7 tablas: `Users`, `Agents`, `AgentPrompts`, `AgentTools`, `ConversationSessions`, `MessageAuditLog`, `RefreshTokens`.
  - [ ] La tabla `AgentPrompts` tiene la constraint `UQ_AgentPrompt_Version` (verificar en `INFORMATION_SCHEMA.TABLE_CONSTRAINTS`).
  - [ ] El índice `IX_AgentPrompts_AgentId_Active` impide insertar un segundo prompt activo para el mismo agente (insertar dos `AgentPrompts` con `IsActive = 1` y mismo `AgentId` → lanza excepción de constraint).
  - [ ] Tras seed (`002_seed_admin.sql`): `SELECT COUNT(*) FROM Users WHERE Email = 'admin@convagent.local'` = 1.
  - [ ] Tras seed: `SELECT COUNT(*) FROM Agents WHERE ContextRoute = '/chat/test'` = 1.
  - [ ] Tras seed: `SELECT COUNT(*) FROM AgentPrompts WHERE IsActive = 1` = 1.
  - [ ] Ejecutar DbUp dos veces seguidas → no lanza excepción (idempotente).
  - [ ] Segunda ejecución: log de DbUp indica que no hay scripts nuevos.
- **Completado cuando:** test compila y falla (rojo).

### TASK-01-C2 · Crear scripts SQL y configurar DbUp en Program.cs

**Archivo:** `src/ConversationalAgent.Infrastructure/Migrations/001_initial_schema.sql`
(marcar como `EmbeddedResource` en el `.csproj`)

```sql
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
    Name         NVARCHAR(200) NOT NULL,
    Description  NVARCHAR(1000) NULL,
    ContextRoute NVARCHAR(500) NOT NULL,
    IsActive     BIT           NOT NULL DEFAULT 1,
    CreatedAt    DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt    DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
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

-- Solo un prompt activo por agente en todo momento (constitution §3.2)
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
(marcar como `EmbeddedResource` en el `.csproj`)

```sql
-- Idempotente: usa IF NOT EXISTS en cada inserción.

-- Usuario admin (password: Admin1234! hasheado con BCrypt, cost factor 12)
-- Generar hash real con: Console.WriteLine(BCrypt.Net.BCrypt.HashPassword("Admin1234!", 12));
IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'admin@convagent.local')
BEGIN
    INSERT INTO Users (Email, PasswordHash, DisplayName, Role, IsActive)
    VALUES (
        'admin@convagent.local',
        '$2a$12$REEMPLAZAR_CON_HASH_REAL_GENERADO_OFFLINE',
        'Administrador',
        'Admin',
        1
    );
END

-- Agente de prueba
IF NOT EXISTS (SELECT 1 FROM Agents WHERE ContextRoute = '/chat/test')
BEGIN
    INSERT INTO Agents (Name, Description, ContextRoute, IsActive)
    VALUES (
        'Agente de Prueba',
        'Agente para verificar el funcionamiento del sistema durante desarrollo.',
        '/chat/test',
        1
    );
END

-- Prompt activo del agente de prueba (Version 1, IsActive = 1)
IF NOT EXISTS (
    SELECT 1 FROM AgentPrompts ap
    INNER JOIN Agents a ON a.Id = ap.AgentId
    WHERE a.ContextRoute = '/chat/test' AND ap.Version = 1
)
BEGIN
    INSERT INTO AgentPrompts (AgentId, Version, SystemPrompt, IsActive, CreatedBy)
    SELECT
        a.Id,
        1,
        'Eres un asistente de prueba. Responde de forma concisa y útil.',
        1,
        u.Id
    FROM Agents a, Users u
    WHERE a.ContextRoute = '/chat/test'
      AND u.Email = 'admin@convagent.local';
END
```

**Importante:** Agregar al `.csproj` de Infrastructure para que los SQL queden embebidos:

```xml
<ItemGroup>
  <EmbeddedResource Include="Migrations\*.sql" />
</ItemGroup>
```

**Fragmento de `Program.cs` para DbUp** (se completa en TASK-01-D2):

```csharp
var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection no está configurada.");

var upgrader = DeployChanges.To
    .SqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(
        typeof(SqlConnectionFactory).Assembly,
        s => s.StartsWith("ConversationalAgent.Infrastructure.Migrations."))
    .WithTransaction()
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful)
{
    Console.Error.WriteLine($"Migración fallida: {result.Error}");
    Environment.Exit(1);
}
```

> **Nota sobre el hash BCrypt del seed:** El placeholder `$2a$12$REEMPLAZAR...` debe sustituirse por el hash real antes de la primera ejecución. Generarlo con:
> ```csharp
> Console.WriteLine(BCrypt.Net.BCrypt.HashPassword("Admin1234!", 12));
> ```
> Este valor es solo para desarrollo local — nunca usar en producción.

- **Completado cuando:** tests de TASK-01-C1 pasan (verde).

---

## Bloque D — Health Check endpoint (spec §7)

### TASK-01-D1 · Test: `GET /health` responde correctamente

- **Archivo test:** `tests/ConversationalAgent.Api.Tests/HealthCheckTests.cs`
- **Tecnología:** `WebApplicationFactory<Program>` + cliente HTTP real.
- **Casos:**
  - [ ] `GET /health` → `200 OK`.
  - [ ] Body contiene `"status"` con valor `"Healthy"`.
  - [ ] `GET /health` **no** requiere token JWT (sin `Authorization` header → igual `200 OK`).
- **Completado cuando:** test compila y falla (rojo).

### TASK-01-D2 · Implementar `Program.cs` mínimo con DbUp, DI y health check

```csharp
// src/ConversationalAgent.Api/Program.cs

using ConversationalAgent.Infrastructure.Data;
using DbUp;

var builder = WebApplication.CreateBuilder(args);

// Health checks
builder.Services.AddHealthChecks();

// DbConnectionFactory
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

var app = builder.Build();

// Migraciones — deben correr antes de mapear endpoints
var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection no está configurada.");

var upgrader = DeployChanges.To
    .SqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(
        typeof(SqlConnectionFactory).Assembly,
        s => s.StartsWith("ConversationalAgent.Infrastructure.Migrations."))
    .WithTransaction()
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful)
{
    Console.Error.WriteLine($"Migración fallida: {result.Error}");
    Environment.Exit(1);
}

// Endpoints
app.MapHealthChecks("/health");

// (Aquí irán los endpoints y hubs en specs posteriores)

app.Run();
```

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
    "MaxRetries": 3
  }
}
```

**Archivo:** `src/ConversationalAgent.Api/appsettings.Development.json` (en `.gitignore` — valores reales locales):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=ConvAgent;User Id=sa;Password=TU_PASSWORD;TrustServerCertificate=True;"
  },
  "Jwt": {
    "SecretKey": "una-clave-larga-de-al-menos-32-caracteres-para-desarrollo"
  },
  "Claude": {
    "ApiKey": "sk-ant-..."
  }
}
```

- **Completado cuando:** tests de TASK-01-D1 pasan (verde) y `GET /health` responde `200 OK` localmente.

---

## Bloque E — Docker Compose y Dockerfiles (RF-08)

> Este bloque no tiene tests unitarios — la verificación es operacional (CA-12, CA-13).
> El SQL Server de desarrollo es la instancia del host — **no** se levanta contenedor de BD (constitution §13.1).

### TASK-01-E1 · Crear Dockerfile del backend (multi-stage)

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

### TASK-01-E2 · Crear Dockerfile del frontend (multi-stage) + nginx.conf

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
        proxy_set_header   Host              $host;
        proxy_set_header   X-Real-IP         $remote_addr;
    }

    location /hubs/ {
        proxy_pass         http://api:8080/hubs/;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade           $http_upgrade;
        proxy_set_header   Connection        "upgrade";
        proxy_set_header   Host              $host;
        proxy_set_header   X-Real-IP         $remote_addr;
        proxy_read_timeout 3600s;
    }
}
```

### TASK-01-E3 · Crear `docker-compose.yml` y `.env.example`

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
    extra_hosts:
      - "host.docker.internal:host-gateway"

  frontend:
    build:
      context: ./frontend
      dockerfile: Dockerfile
    ports:
      - "3000:80"
    depends_on:
      - api
```

**Archivo:** `.env.example` (raíz del repo)

```
# Conexión al SQL Server del host (usado por los contenedores Docker)
# La API corre en Docker y se conecta al SQL Server local del host vía host.docker.internal
DB_USER=sa
DB_PASSWORD=

# Clave secreta para firmar JWT (mínimo 32 caracteres)
# Generar con: openssl rand -base64 32
JWT_SECRET_KEY=

# API Key de Anthropic — obtener en console.anthropic.com
CLAUDE_API_KEY=sk-ant-...

# URLs del frontend (Vite build — solo para desarrollo local fuera de Docker)
VITE_API_BASE_URL=http://localhost:5000
VITE_SIGNALR_HUB_URL=http://localhost:5000/hubs/conversation
```

- **Completado cuando:** `docker compose up --build` levanta ambos servicios sin errores y `GET http://localhost:5000/health` retorna `200 OK` con las migraciones ejecutadas.

---

## Bloque F — Scaffolding del Frontend React (RF-07)

> Verificación visual y de compilación — no hay tests unitarios en este bloque.

### TASK-01-F1 · Inicializar proyecto Vite + React + TypeScript

```powershell
# Desde la raíz del repositorio
npm create vite@latest frontend -- --template react-ts
cd frontend
npm install
```

Verificar que `tsconfig.json` tiene `"strict": true` y `"noImplicitAny": true`.

### TASK-01-F2 · Instalar y configurar Tailwind CSS

```powershell
npm install -D tailwindcss postcss autoprefixer tailwindcss-animate
npx tailwindcss init -p
```

**Archivo:** `frontend/tailwind.config.ts`:

```typescript
import type { Config } from 'tailwindcss'

const config: Config = {
  darkMode: ['class'],
  content: [
    './index.html',
    './src/**/*.{ts,tsx}',
  ],
  theme: {
    extend: {
      colors: {
        border: 'hsl(var(--border))',
        input: 'hsl(var(--input))',
        ring: 'hsl(var(--ring))',
        background: 'hsl(var(--background))',
        foreground: 'hsl(var(--foreground))',
        primary: {
          DEFAULT: 'hsl(var(--primary))',
          foreground: 'hsl(var(--primary-foreground))',
        },
        secondary: {
          DEFAULT: 'hsl(var(--secondary))',
          foreground: 'hsl(var(--secondary-foreground))',
        },
        muted: {
          DEFAULT: 'hsl(var(--muted))',
          foreground: 'hsl(var(--muted-foreground))',
        },
        accent: {
          DEFAULT: 'hsl(var(--accent))',
          foreground: 'hsl(var(--accent-foreground))',
        },
        destructive: {
          DEFAULT: 'hsl(var(--destructive))',
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

### TASK-01-F3 · Inicializar shadcn/ui y agregar componente Button

```powershell
npx shadcn@latest init
npx shadcn@latest add button
```

Verificar que `frontend/src/components/ui/button.tsx` existe — **no editarlo directamente** (constitution §11.6).

### TASK-01-F4 · Crear la estructura de carpetas definitiva (constitution §11.1)

```powershell
# Desde frontend/src/
New-Item -ItemType Directory -Force -Path `
  api, hub, `
  components/chat, components/admin, components/layout, `
  pages, stores, hooks, types, lib
```

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

**Archivo:** `frontend/src/types/conversation.ts` (tipos canónicos — constitution §11.2):

```typescript
export interface ConversationMessage {
  role: 'user' | 'assistant';
  content: string;
  timestamp: string;
}

export interface ConversationSession {
  sessionId: string;
  contextRoute: string;
  agentName: string;
  messages: ConversationMessage[];
  startedAt: string;
}
```

### TASK-01-F5 · Configurar `vite.config.ts` con proxy y alias `@/`

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

### TASK-01-F6 · Página placeholder en `App.tsx`

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

- **Completado cuando:** `npm run build` completa con 0 errores de TypeScript y `npm run dev` muestra el texto `"Agente Conversacional IA"` con el `Button` de shadcn/ui visible en `http://localhost:5173`.

---

## Bloque G — Configuración y .gitignore (RF-06)

> Sin tests — verificación con `git status`.

### TASK-01-G1 · Crear `.gitignore`

**Archivo:** `.gitignore` (raíz del repo)

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

- **Criterio:** `git status` no muestra `.env` ni `appsettings.Development.json` aunque los archivos existan localmente (CA-14).

---

## Bloque H — Verificación Final

### TASK-01-H1 · Build .NET sin errores ni warnings

```powershell
dotnet build ConversationalAgent.sln
```

- **Criterio:** `0 Error(s)  0 Warning(s)` (CA-01).

### TASK-01-H2 · Todos los tests de infrastructure pasan (verde)

```powershell
dotnet test tests/ConversationalAgent.Infrastructure.Tests/ConversationalAgent.Infrastructure.Tests.csproj --logger "console;verbosity=normal"
```

- **Criterio:** `SqlConnectionFactoryTests` y `MigrationTests` pasan. (CA-05, CA-06, CA-07, CA-08, CA-10)

### TASK-01-H3 · Tests de API pasan (verde)

```powershell
dotnet test tests/ConversationalAgent.Api.Tests/ConversationalAgent.Api.Tests.csproj --logger "console;verbosity=normal"
```

- **Criterio:** `HealthCheckTests` pasan. (CA-09)

### TASK-01-H4 · Tests triviales de Domain y Application pasan

```powershell
dotnet test tests/ConversationalAgent.Domain.Tests/ConversationalAgent.Domain.Tests.csproj
dotnet test tests/ConversationalAgent.Application.Tests/ConversationalAgent.Application.Tests.csproj
```

- **Criterio:** Los placeholders se descubren y pasan. (CA-02)

### TASK-01-H5 · Suite completa de tests

```powershell
dotnet test ConversationalAgent.sln --logger "console;verbosity=normal"
```

- **Criterio:** Todos los proyectos de test reportan `Passed`.

### TASK-01-H6 · Entorno Docker completo levanta en un solo comando

```powershell
# Desde la raíz — copiar .env.example a .env y rellenar DB_USER, DB_PASSWORD, JWT_SECRET_KEY, CLAUDE_API_KEY
Copy-Item .env.example .env
docker compose up --build
```

- **Verificaciones manuales:**

| CA | Check | Comando de verificación |
|---|---|---|
| CA-03 | DbUp ejecuta 2 scripts al arrancar | Revisar logs de arranque del contenedor `api` |
| CA-04 | DbUp no re-ejecuta scripts en segunda arrancada | Reiniciar contenedor `api` y revisar logs |
| CA-05 | 7 tablas creadas | `SELECT TABLE_NAME FROM ConvAgent.INFORMATION_SCHEMA.TABLES` |
| CA-06 | Índice filtrado impide 2 prompts activos | Cubierto en test de integración (TASK-01-C1) |
| CA-07 | Seed crea admin, agente y prompt | `SELECT COUNT(*) FROM Users WHERE Email = 'admin@convagent.local'` → 1 |
| CA-08 | Seed idempotente | Reiniciar contenedor y verificar counts estables |
| CA-09 | `GET /health` → 200 OK | `curl http://localhost:5000/health` |
| CA-12 | `docker compose up` levanta api y frontend | `docker compose ps` |
| CA-13 | Contenedor api se conecta al SQL Server del host | Revisar logs de DbUp — migrations successful |
| CA-14 | `.env` y `appsettings.Development.json` no aparecen en git | `git status` |

---

## Resumen de archivos a crear en SPEC-01

| # | Archivo | Bloque |
|---|---|---|
| 1 | `ConversationalAgent.sln` | A |
| 2 | `src/ConversationalAgent.Domain/ConversationalAgent.Domain.csproj` | A |
| 3 | `src/ConversationalAgent.Application/ConversationalAgent.Application.csproj` | A |
| 4 | `src/ConversationalAgent.Infrastructure/ConversationalAgent.Infrastructure.csproj` | A |
| 5 | `src/ConversationalAgent.Api/ConversationalAgent.Api.csproj` | A |
| 6 | `tests/ConversationalAgent.Domain.Tests/PlaceholderTest.cs` | A |
| 7 | `tests/ConversationalAgent.Application.Tests/PlaceholderTest.cs` | A |
| 8 | `tests/ConversationalAgent.Infrastructure.Tests/PlaceholderTest.cs` | A |
| 9 | `tests/ConversationalAgent.Api.Tests/PlaceholderTest.cs` | A |
| 10 | `src/ConversationalAgent.Infrastructure/Data/IDbConnectionFactory.cs` | B |
| 11 | `src/ConversationalAgent.Infrastructure/Data/SqlConnectionFactory.cs` | B |
| 12 | `tests/ConversationalAgent.Infrastructure.Tests/Data/SqlConnectionFactoryTests.cs` | B |
| 13 | `src/ConversationalAgent.Infrastructure/Migrations/001_initial_schema.sql` | C |
| 14 | `src/ConversationalAgent.Infrastructure/Migrations/002_seed_admin.sql` | C |
| 15 | `tests/ConversationalAgent.Infrastructure.Tests/Migrations/MigrationTests.cs` | C |
| 16 | `src/ConversationalAgent.Api/Program.cs` | D |
| 17 | `src/ConversationalAgent.Api/appsettings.json` | D |
| 18 | `src/ConversationalAgent.Api/appsettings.Development.json` | D (gitignored) |
| 19 | `tests/ConversationalAgent.Api.Tests/HealthCheckTests.cs` | D |
| 20 | `src/ConversationalAgent.Api/Dockerfile` | E |
| 21 | `frontend/Dockerfile` | E |
| 22 | `frontend/nginx.conf` | E |
| 23 | `docker-compose.yml` | E |
| 24 | `.env.example` | E |
| 25 | `frontend/vite.config.ts` | F |
| 26 | `frontend/tailwind.config.ts` | F |
| 27 | `frontend/tsconfig.json` | F |
| 28 | `frontend/src/index.css` | F |
| 29 | `frontend/src/App.tsx` | F |
| 30 | `frontend/src/lib/utils.ts` | F |
| 31 | `frontend/src/types/conversation.ts` | F |
| 32 | `frontend/src/components/ui/button.tsx` | F (shadcn CLI) |
| 33 | `.gitignore` | G |

---

## Checklist SPEC-01

### Bloque A — Estructura de solución
- [ ] TASK-01-A1 · Solución .NET con 4 proyectos src + 4 proyectos test, referencias y NuGet configurados

### Bloque B — IDbConnectionFactory
- [ ] TASK-01-B1 · Test `SqlConnectionFactory` (rojo)
- [ ] TASK-01-B2 · Impl `IDbConnectionFactory` + `SqlConnectionFactory` (verde)

### Bloque C — Migraciones DbUp
- [ ] TASK-01-C1 · Test de integración de migraciones con Testcontainers (rojo)
- [ ] TASK-01-C2 · Scripts `001_initial_schema.sql` y `002_seed_admin.sql` + EmbeddedResource (verde)

### Bloque D — Health Check
- [ ] TASK-01-D1 · Test `GET /health` con WebApplicationFactory (rojo)
- [ ] TASK-01-D2 · `Program.cs` con DbUp + DI + `app.MapHealthChecks` + `appsettings.json` (verde)

### Bloque E — Docker Compose
- [ ] TASK-01-E1 · Dockerfile del backend (multi-stage)
- [ ] TASK-01-E2 · Dockerfile del frontend (multi-stage) + nginx.conf con proxy WS para SignalR
- [ ] TASK-01-E3 · `docker-compose.yml` con host.docker.internal + `.env.example`

### Bloque F — Frontend React
- [ ] TASK-01-F1 · Vite + React + TypeScript inicializado con strict mode
- [ ] TASK-01-F2 · Tailwind CSS configurado con variables CSS de shadcn/ui
- [ ] TASK-01-F3 · shadcn/ui inicializado + componente Button
- [ ] TASK-01-F4 · Estructura de carpetas definitiva + `lib/utils.ts` + `types/conversation.ts`
- [ ] TASK-01-F5 · `vite.config.ts` con proxy `/api` y `/hubs` (WebSocket) y alias `@/`
- [ ] TASK-01-F6 · `App.tsx` con texto "Agente Conversacional IA" y Button visible

### Bloque G — Secretos y .gitignore
- [ ] TASK-01-G1 · `.gitignore` que excluye `.env` y `appsettings.Development.json`

### Bloque H — Verificación Final
- [ ] TASK-01-H1 · `dotnet build` → 0 errores, 0 warnings
- [ ] TASK-01-H2 · Tests de Infrastructure pasan (verde) — CA-05, CA-06, CA-07, CA-08, CA-10
- [ ] TASK-01-H3 · Tests de Api pasan (verde) — CA-09
- [ ] TASK-01-H4 · Tests triviales de Domain y Application pasan — CA-02
- [ ] TASK-01-H5 · `dotnet test` completo → todos pasan
- [ ] TASK-01-H6 · `docker compose up --build` → api + frontend healthy, migraciones ejecutadas — CA-03, CA-04, CA-12, CA-13, CA-14
