# spec-01-foundation.md
# Feature: Fundación — Solución, Migraciones y Configuración

## 1. Resumen

Crear el esqueleto completo y correcto del proyecto: estructura de solución
.NET 9 con 4 proyectos, esquema completo de base de datos con todas las
tablas del dominio, datos semilla mínimos para trabajar desde SPEC-02,
scaffolding del frontend React, y Docker Compose para levantar API y
frontend apuntando al SQL Server local del host. Al terminar este spec,
el proyecto compila sin errores, las migraciones corren automáticamente
al arrancar la API, y existe un usuario admin y un agente de prueba en
base de datos.

---

## 2. Motivación

Sin una estructura de solución correcta y sin las tablas en base de datos,
ningún spec posterior puede avanzar. Este spec es el único que no tiene
requisito previo — todos los demás dependen de él. El objetivo es una base
sólida: compilación limpia, pipeline de tests funcionando, migraciones
automatizadas y contenedores operativos, sin ninguna lógica de negocio
todavía. Las carpetas y namespaces creados aquí son los que usarán todos
los specs siguientes — no se pueden cambiar después sin romper referencias.

---

## 3. Usuarios y Casos de Uso

| Actor | Caso de uso |
|---|---|
| Desarrollador | Clonar el repositorio y levantar el proyecto sin configuración manual de BD |
| Desarrollador | Correr todos los tests desde el primer commit y ver que pasan |
| Desarrollador | Correr `docker compose up` y tener API + frontend disponibles |
| Sistema (API) | Ejecutar migraciones pendientes automáticamente al arrancar |
| Sistema (API) | Leer configuración sensible desde variables de entorno, nunca desde código |

---

## 4. Requisitos Funcionales

### RF-01 — Estructura de solución .NET
- La solución `ConversationalAgent.sln` contiene exactamente 4 proyectos
  en `src/`:
  - `ConversationalAgent.Domain` (Class Library)
  - `ConversationalAgent.Application` (Class Library)
  - `ConversationalAgent.Infrastructure` (Class Library)
  - `ConversationalAgent.Api` (ASP.NET Core Web API)
- Referencias entre proyectos:
  - `Application` → `Domain`
  - `Infrastructure` → `Domain`
  - `Api` → `Application` + `Infrastructure`
  - `Domain` no referencia ningún otro proyecto del solution.
- Cada proyecto tiene su proyecto de tests correspondiente en `tests/`:
  `Domain.Tests`, `Application.Tests`, `Infrastructure.Tests`, `Api.Tests`.

### RF-02 — Carpetas base en cada proyecto
Las siguientes carpetas deben existir vacías (con `.gitkeep`) desde el
inicio para que los specs siguientes las usen sin crearlas:

**Domain:**
- `Entities/`
- `Interfaces/`
- `LLM/`
- `Models/`
- `SignalR/`
- `Exceptions/`

**Application:**
- `UseCases/`
- `Services/`

**Infrastructure:**
- `Data/`
- `Cache/`
- `Prompts/`
- `Guardrails/`
- `Sql/`
- `Migrations/`
- `Auth/`
- `LLM/`
- `Tools/`

**Api:**
- `Endpoints/`
- `Hubs/`

### RF-03 — Migraciones con DbUp
- `DbUp` se configura en `Program.cs` y corre al arrancar la API, antes
  de que cualquier endpoint esté disponible.
- Los scripts `.sql` viven en
  `src/ConversationalAgent.Infrastructure/Migrations/` y se embeben como
  recursos del ensamblado (`EmbeddedResource` en el `.csproj`).
- Si una migración falla, la API no arranca: loggea el error y retorna
  exit code no-cero.
- Los scripts se ejecutan en orden lexicográfico estricto por nombre de
  archivo.
- DbUp no re-ejecuta scripts ya aplicados — es idempotente por diseño.

### RF-04 — Esquema completo de base de datos
La migración `001_initial_schema.sql` crea las 6 tablas del dominio con
todos sus constraints e índices. Ver sección 8 para el SQL completo.

Invariantes que deben estar garantizadas por el esquema:
- Solo un `AgentPrompt` activo por agente en todo momento — índice único
  filtrado `WHERE IsActive = 1`.
- `ContextRoute` único entre agentes.
- `MessageAuditLog` solo almacena hash SHA-256 — nunca texto en claro.
- Todas las foreign keys declaradas explícitamente.

### RF-05 — Datos semilla
La migración `002_seed_admin.sql` inserta con `IF NOT EXISTS` (idempotente):
- Usuario admin: `admin@convagent.local` / password `Admin1234!`
  (hash BCrypt cost 12, generado offline e incluido en el script).
- Agente de prueba: nombre `"Agente de Prueba"`,
  `ContextRoute = '/chat/test'`, activo.
- `AgentPrompt` versión 1 activo para ese agente con texto:
  `"Eres un asistente de prueba. Responde de forma concisa y útil."`.

### RF-06 — IDbConnectionFactory
- Interfaz `IDbConnectionFactory` en `Infrastructure/Data/`.
- Implementación `SqlConnectionFactory`: lee
  `ConnectionStrings:DefaultConnection` de `IConfiguration`. Lanza
  `InvalidOperationException` si no está configurada.
- `IDbConnectionFactory.Create()` devuelve un `IDbConnection` abierto.
- Registrado en DI como `Singleton`.

### RF-07 — Configuración y secretos
- `appsettings.json`: estructura completa con valores vacíos — nunca
  valores reales.
- `appsettings.Development.json`: en `.gitignore`, contiene valores
  reales para desarrollo local.
- `.env.example`: documenta todas las variables de entorno requeridas.
- `.env`: en `.gitignore`.
- `appsettings.Development.json` y `.env` nunca aparecen en `git status`.

### RF-08 — Scaffolding del frontend
- Proyecto `frontend/` inicializado con Vite + React + TypeScript.
- Tailwind CSS configurado: `tailwind.config.ts` y directivas en
  `index.css`.
- shadcn/ui inicializado: `components.json` presente, componente `Button`
  instalado como smoke test del setup.
- Carpetas base creadas vacías (con `.gitkeep`):
  `src/api/`, `src/hub/`, `src/components/ui/`, `src/components/chat/`,
  `src/components/admin/`, `src/components/layout/`, `src/pages/`,
  `src/stores/`, `src/hooks/`, `src/types/`, `src/lib/`.
- `App.tsx` muestra un texto placeholder: `"Agente Conversacional IA"`.
- `npm run build` y `npm run dev` corren sin errores ni warnings.
- `tsconfig.json` tiene `"strict": true` y `"noImplicitAny": true`.

### RF-09 — Docker Compose
- `docker-compose.yml` levanta `api` y `frontend` — sin servicio de BD.
- El servicio `api` usa `host.docker.internal` para conectarse al SQL
  Server local del host.
- `extra_hosts: host-gateway` configurado para compatibilidad con Linux.
- El servicio `redis` se levanta con imagen `redis:7-alpine`.
- Puertos: `5000:8080` (api), `3000:80` (frontend), `6379:6379` (redis).
- Variables sensibles desde `.env` — nunca hardcodeadas en el compose.

### RF-10 — Health check
- `GET /health` retorna `200 OK` con `{"status":"Healthy"}`.
- No requiere autenticación.
- Configurado con `app.MapHealthChecks("/health")`.

### RF-11 — Pipeline de tests
- Cada proyecto de tests tiene al menos un test trivial que verifica que
  xUnit puede descubrir y correr tests.
- `dotnet test` en la raíz corre los 4 proyectos y todos pasan.

---

## 5. Requisitos No Funcionales

- **Reproducibilidad**: clonar + configurar `.env` y
  `appsettings.Development.json` + `dotnet run` = API operativa sin pasos
  manuales adicionales.
- **Idempotencia de migraciones**: DbUp múltiples veces contra la misma
  BD no genera errores ni datos duplicados.
- **Compilación limpia**: `dotnet build` con 0 errores y 0 warnings.
- **TypeScript estricto**: `npm run build` con 0 errores de tipo.
- **Sin lógica de negocio**: este spec no implementa ningún endpoint real,
  ningún use case, ningún repositorio funcional más allá de
  `IDbConnectionFactory`. Solo estructura e infraestructura.

---

## 6. Modelo de Dominio

No hay entidades de dominio en este spec. Solo la interfaz base de
infraestructura de datos:

```csharp
// src/ConversationalAgent.Infrastructure/Data/IDbConnectionFactory.cs
namespace ConversationalAgent.Infrastructure.Data;

public interface IDbConnectionFactory
{
    /// <summary>
    /// Crea y abre una nueva conexión a SQL Server.
    /// El llamador es responsable de hacer Dispose.
    /// </summary>
    IDbConnection Create();
}

// src/ConversationalAgent.Infrastructure/Data/SqlConnectionFactory.cs
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

---

## 7. Contrato de API

Solo el health check. No hay endpoints funcionales en este spec.

### GET `/health`

Verifica que la API arrancó correctamente.

**No requiere autenticación.**

**Response `200 OK`:**
```json
{
  "status": "Healthy"
}
```

---

## 8. Esquema de Base de Datos

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
    Role         NVARCHAR(20)     NOT NULL,   -- 'user' | 'assistant'
    ContentHash  NVARCHAR(64)     NOT NULL,   -- SHA-256 del contenido
    TokensUsed   INT              NULL,
    ToolsInvoked NVARCHAR(MAX)    NULL,        -- JSON array
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

```sql
-- 002_seed_admin.sql
-- Idempotente: IF NOT EXISTS en cada inserción.

-- ─────────────────────────────────────────────────────────────────
-- IMPORTANTE: reemplazar el hash placeholder antes del primer uso.
-- Generar el hash real con:
--   Console.WriteLine(BCrypt.Net.BCrypt.HashPassword("Admin1234!", 12));
-- y pegar el resultado en lugar de '$2a$12$PLACEHOLDER...'
-- Este hash es solo para desarrollo local — nunca usar en producción.
-- ─────────────────────────────────────────────────────────────────

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

---

## 9. Configuración

### appsettings.json — commiteado, sin valores reales

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

### appsettings.Development.json — en .gitignore, valores reales locales

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

> Con `Redis__Enabled = false` en desarrollo local la API arranca sin
> necesitar Redis. Se usa `InMemoryAgentCacheService` en su lugar
> (se registrará en SPEC-03).

### .env.example — commiteado, documenta variables para Docker

```
# Conexión al SQL Server del host desde contenedores Docker
DB_USER=sa
DB_PASSWORD=

# JWT
JWT_SECRET_KEY=

# Anthropic Claude API
CLAUDE_API_KEY=sk-ant-...

# Redis (dentro de Docker apunta al servicio redis del compose)
REDIS_CONNECTION_STRING=redis:6379

# Frontend (Vite)
VITE_API_BASE_URL=http://localhost:5000
VITE_SIGNALR_HUB_URL=http://localhost:5000/hubs/conversation
```

---

## 10. Criterios de Aceptación

| ID | Criterio | Verificación |
|---|---|---|
| CA-01 | `dotnet build` completa con 0 errores y 0 warnings en todos los proyectos | CI / manual |
| CA-02 | `dotnet test` descubre y corre el test trivial de cada proyecto, todos pasan | CI / manual |
| CA-03 | Al arrancar la API por primera vez, DbUp ejecuta los 2 scripts y loggea su ejecución | Manual: revisar logs de arranque |
| CA-04 | Al arrancar la API una segunda vez, DbUp no re-ejecuta los scripts | Manual: revisar logs |
| CA-05 | Las 6 tablas existen con columnas, constraints e índices correctos | Test de integración (Testcontainers) |
| CA-06 | El índice filtrado `IX_AgentPrompts_AgentId_Active` impide insertar un segundo prompt activo para el mismo agente | Test de integración SQL |
| CA-07 | El seed crea el usuario admin, el agente de prueba y su prompt activo | Test de integración SQL |
| CA-08 | El seed es idempotente: correrlo dos veces no duplica datos | Test de integración SQL |
| CA-09 | `GET /health` retorna `200 OK` con `{"status":"Healthy"}` | Manual / test API |
| CA-10 | `SqlConnectionFactory` lanza `InvalidOperationException` si la connection string no está configurada | Test unitario |
| CA-11 | `npm run build` completa sin errores de TypeScript ni de Vite | CI / manual |
| CA-12 | `npm run dev` arranca y muestra el texto placeholder en el navegador | Manual |
| CA-13 | `docker compose up` levanta `api`, `frontend` y `redis` sin errores | Manual |
| CA-14 | El contenedor `api` se conecta al SQL Server del host y corre las migraciones | Manual |
| CA-15 | `appsettings.Development.json` y `.env` están en `.gitignore` y no aparecen en `git status` | Manual |
| CA-16 | Las carpetas base de todos los proyectos existen con `.gitkeep` | Manual |

---

## 11. Archivos que este spec genera

```
/
├── ConversationalAgent.sln
│
├── src/
│   ├── ConversationalAgent.Domain/
│   │   ├── ConversationalAgent.Domain.csproj
│   │   ├── Entities/.gitkeep
│   │   ├── Interfaces/.gitkeep
│   │   ├── LLM/.gitkeep
│   │   ├── Models/.gitkeep
│   │   ├── SignalR/.gitkeep
│   │   └── Exceptions/.gitkeep
│   │
│   ├── ConversationalAgent.Application/
│   │   ├── ConversationalAgent.Application.csproj
│   │   ├── UseCases/.gitkeep
│   │   └── Services/.gitkeep
│   │
│   ├── ConversationalAgent.Infrastructure/
│   │   ├── ConversationalAgent.Infrastructure.csproj
│   │   ├── Data/
│   │   │   ├── IDbConnectionFactory.cs
│   │   │   └── SqlConnectionFactory.cs
│   │   ├── Cache/.gitkeep
│   │   ├── Prompts/.gitkeep
│   │   ├── Guardrails/.gitkeep
│   │   ├── Sql/.gitkeep
│   │   ├── Migrations/
│   │   │   ├── 001_initial_schema.sql   ← EmbeddedResource
│   │   │   └── 002_seed_admin.sql       ← EmbeddedResource
│   │   ├── Auth/.gitkeep
│   │   ├── LLM/.gitkeep
│   │   └── Tools/.gitkeep
│   │
│   └── ConversationalAgent.Api/
│       ├── ConversationalAgent.Api.csproj
│       ├── Endpoints/.gitkeep
│       ├── Hubs/.gitkeep
│       └── Program.cs
│
├── tests/
│   ├── ConversationalAgent.Domain.Tests/
│   │   ├── ConversationalAgent.Domain.Tests.csproj
│   │   └── PlaceholderTest.cs
│   ├── ConversationalAgent.Application.Tests/
│   │   ├── ConversationalAgent.Application.Tests.csproj
│   │   └── PlaceholderTest.cs
│   ├── ConversationalAgent.Infrastructure.Tests/
│   │   ├── ConversationalAgent.Infrastructure.Tests.csproj
│   │   ├── PlaceholderTest.cs
│   │   └── Migrations/
│   │       └── MigrationTests.cs        ← Testcontainers SQL Server
│   └── ConversationalAgent.Api.Tests/
│       ├── ConversationalAgent.Api.Tests.csproj
│       └── PlaceholderTest.cs
│
├── frontend/
│   ├── src/
│   │   ├── api/.gitkeep
│   │   ├── hub/.gitkeep
│   │   ├── components/
│   │   │   ├── ui/.gitkeep
│   │   │   ├── chat/.gitkeep
│   │   │   ├── admin/.gitkeep
│   │   │   └── layout/.gitkeep
│   │   ├── pages/.gitkeep
│   │   ├── stores/.gitkeep
│   │   ├── hooks/.gitkeep
│   │   ├── types/.gitkeep
│   │   ├── lib/.gitkeep
│   │   ├── App.tsx
│   │   ├── main.tsx
│   │   └── index.css
│   ├── components.json              ← shadcn/ui config
│   ├── index.html
│   ├── package.json
│   ├── vite.config.ts
│   ├── tailwind.config.ts
│   └── tsconfig.json
│
├── docker-compose.yml
├── .env.example
├── .gitignore
└── README.md
```

### Paquetes NuGet por proyecto

**ConversationalAgent.Infrastructure.csproj:**
```xml
<PackageReference Include="Dapper"                    Version="2.*" />
<PackageReference Include="Microsoft.Data.SqlClient"  Version="5.*" />
<PackageReference Include="dbup-sqlserver"             Version="5.*" />
<PackageReference Include="BCrypt.Net-Next"            Version="4.*" />
```

**ConversationalAgent.Api.csproj:**
```xml
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.*" />
<PackageReference Include="Scalar.AspNetCore"            Version="1.*" />
```

**ConversationalAgent.Infrastructure.Tests.csproj:**
```xml
<PackageReference Include="xunit"                        Version="2.*" />
<PackageReference Include="xunit.runner.visualstudio"    Version="2.*" />
<PackageReference Include="FluentAssertions"             Version="6.*" />
<PackageReference Include="Testcontainers.MsSql"         Version="3.*" />
```

**Todos los proyectos de test:**
```xml
<PackageReference Include="xunit"                     Version="2.*" />
<PackageReference Include="xunit.runner.visualstudio"  Version="2.*" />
<PackageReference Include="FluentAssertions"           Version="6.*" />
<PackageReference Include="Moq"                        Version="4.*" />
```

### Dependencias npm (frontend)

```json
{
  "dependencies": {
    "react": "^18.3.1",
    "react-dom": "^18.3.1"
  },
  "devDependencies": {
    "@types/react": "^18.3.1",
    "@types/react-dom": "^18.3.1",
    "@vitejs/plugin-react": "^4.3.1",
    "autoprefixer": "^10.4.20",
    "postcss": "^8.4.47",
    "tailwindcss": "^3.4.14",
    "typescript": "^5.6.2",
    "vite": "^5.4.10"
  }
}
```

shadcn/ui se instala vía CLI después de `npm install`:
```bash
npx shadcn@latest init
npx shadcn@latest add button
```

---

## 12. Program.cs — Estructura esperada

```csharp
// src/ConversationalAgent.Api/Program.cs
using ConversationalAgent.Infrastructure.Data;
using DbUp;

var builder = WebApplication.CreateBuilder(args);

// ── Servicios base ────────────────────────────────────────────────
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

// ── (SPEC-02 agregará JWT y Auth aquí) ───────────────────────────
// ── (SPEC-03 agregará Redis / Cache aquí) ────────────────────────
// ── (SPEC-05 agregará SignalR aquí) ──────────────────────────────

var app = builder.Build();

// ── Migraciones DbUp — antes de mapear endpoints ─────────────────
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

// ── Endpoints ────────────────────────────────────────────────────
app.MapHealthChecks("/health");

// ── (SPEC-02 agregará endpoints de auth aquí) ────────────────────
// ── (SPEC-05 agregará MapHub<ConversationHub> aquí) ──────────────

app.Run();

// Necesario para TestServer en Api.Tests
public partial class Program { }
```

---

## 13. docker-compose.yml — Estructura esperada

```yaml
services:
  api:
    build:
      context: ./src
      dockerfile: ConversationalAgent.Api/Dockerfile
    ports:
      - "5000:8080"
    environment:
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

---

## 14. README.md — Secciones obligatorias

1. **Requisitos previos**: .NET 9 SDK, Node.js 20+, SQL Server local
   (instancia corriendo en `localhost`), Docker Desktop, Redis
   (opcional para desarrollo local — `Redis__Enabled=false` lo omite).
2. **Configuración inicial**:
   - Crear `src/ConversationalAgent.Api/appsettings.Development.json`
     a partir del ejemplo en la sección 9.
   - Crear `.env` a partir de `.env.example`.
3. **Generar el hash BCrypt del seed**:
   ```csharp
   // Correr como script o en Program.cs temporalmente:
   Console.WriteLine(BCrypt.Net.BCrypt.HashPassword("Admin1234!", 12));
   // Copiar el resultado y reemplazarlo en 002_seed_admin.sql
   ```
4. **Levantar sin Docker**: `dotnet run --project src/ConversationalAgent.Api`
   + `cd frontend && npm run dev`.
5. **Levantar con Docker**: `docker compose up --build`.
6. **Correr tests**: `dotnet test`.
7. **Verificar migraciones**: revisar los logs de arranque de la API —
   DbUp loggea cada script ejecutado.
