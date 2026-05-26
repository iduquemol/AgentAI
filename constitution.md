# constitution.md
# Agente Conversacional IA — Principios No Negociables

## 1. Identidad del Proyecto

Esta es una aplicación web full-stack que expone un agente de IA conversacional
en tiempo real. El frontend en React se comunica con el backend vía SignalR
(WebSocket) para streaming de mensajes. El backend, en .NET 9, recibe los
mensajes del usuario, carga el system prompt correspondiente desde SQL Server,
construye el historial de conversación enviado por el cliente, y llama a la
API de Claude (Anthropic) vía HTTP.

El comportamiento del agente es 100% configurable en base de datos: ningún
prompt, instrucción ni definición de herramienta está hardcodeada en el código
fuente. El sistema soporta múltiples agentes, cada uno activado automáticamente
según el contexto o la ruta de la conversación. Los tools que el LLM puede
invocar son clases C# registradas en el contenedor de DI, descubiertas
dinámicamente en tiempo de ejecución.

El sistema soporta múltiples usuarios con autenticación JWT. Cada usuario
tiene su propia sesión y contexto de conversación completamente aislados.

---

## 2. Stack Tecnológico Obligatorio

### Backend

| Capa | Tecnología | Versión mínima |
|---|---|---|
| Runtime | .NET | 9.0 |
| Framework web | ASP.NET Core Minimal API | 9.0 |
| Tiempo real | SignalR (ASP.NET Core) | Nativo 9.0 |
| ORM / Data Access | Dapper | 2.x |
| Base de datos | SQL Server | 2022 |
| Migraciones | DbUp | Latest |
| Autenticación | ASP.NET Core Identity + JWT Bearer | Nativo |
| LLM Client | HttpClient nativo (llamada directa a API Anthropic) | Nativo .NET 9 |
| Resiliencia | Polly | 8.x |
| Caché distribuido | Redis + StackExchange.Redis | 7.x |
| Contenedores | Docker + Docker Compose | Latest |
| Testing | xUnit + Moq + FluentAssertions | Latest |

### Frontend

| Capa | Tecnología | Versión mínima |
|---|---|---|
| Framework | React | 18.x |
| Lenguaje | TypeScript | 5.x |
| Bundler | Vite | 5.x |
| Estilos | Tailwind CSS | 3.x |
| Componentes UI | shadcn/ui | Latest |
| Tiempo real | @microsoft/signalr | Latest |
| Estado global | Zustand | Latest |
| Formularios | React Hook Form + Zod | Latest |
| Testing | Vitest + React Testing Library | Latest |

Desviaciones de este stack requieren justificación documentada en el spec
correspondiente.

---

## 3. Modelo de Datos — Reglas No Negociables

### 3.1 Esquema Completo de Tablas

```sql
-- 001_initial_schema.sql

CREATE TABLE Users (
    Id           INT IDENTITY PRIMARY KEY,
    Email        NVARCHAR(256) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(512) NOT NULL,
    DisplayName  NVARCHAR(200) NOT NULL,
    Role         NVARCHAR(50)  NOT NULL DEFAULT 'User',  -- 'User' | 'Admin'
    CreatedAt    DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    IsActive     BIT NOT NULL DEFAULT 1
);

-- Agentes: cada agente tiene un contexto/ruta que lo activa y su prompt
CREATE TABLE Agents (
    Id           INT IDENTITY PRIMARY KEY,
    Name         NVARCHAR(200) NOT NULL,
    Description  NVARCHAR(1000) NULL,
    ContextRoute NVARCHAR(500) NOT NULL UNIQUE,
                 -- Ej: '/chat/soporte', '/chat/ventas'
                 -- El hub SignalR lo usa para resolver el agente correcto
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAt    DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt    DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

-- Versiones de system prompt de cada agente (versionado obligatorio)
CREATE TABLE AgentPrompts (
    Id          INT IDENTITY PRIMARY KEY,
    AgentId     INT NOT NULL REFERENCES Agents(Id),
    Version     INT NOT NULL,
    SystemPrompt NVARCHAR(MAX) NOT NULL,
    IsActive    BIT NOT NULL DEFAULT 0,  -- Solo uno activo por agente
    CreatedBy   INT NOT NULL REFERENCES Users(Id),
    CreatedAt   DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT UQ_AgentPrompt_Version UNIQUE (AgentId, Version)
);
CREATE INDEX IX_AgentPrompts_AgentId_Active ON AgentPrompts(AgentId, IsActive);

-- Tools disponibles: registro de las clases C# de tools habilitadas
CREATE TABLE AgentTools (
    Id           INT IDENTITY PRIMARY KEY,
    AgentId      INT NOT NULL REFERENCES Agents(Id),
    ToolClassName NVARCHAR(300) NOT NULL,
                 -- Nombre completo de la clase C# que implementa ITool
                 -- Ej: 'ConversationalAgent.Infrastructure.Tools.WeatherTool'
    IsEnabled    BIT NOT NULL DEFAULT 1,
    CreatedAt    DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT UQ_AgentTools_AgentTool UNIQUE (AgentId, ToolClassName)
);

-- Sesiones de conversación (una por usuario/agente/tab)
CREATE TABLE ConversationSessions (
    Id          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    UserId      INT NOT NULL REFERENCES Users(Id),
    AgentId     INT NOT NULL REFERENCES Agents(Id),
    StartedAt   DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    LastActivity DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    IsActive    BIT NOT NULL DEFAULT 1
);
CREATE INDEX IX_Sessions_UserId ON ConversationSessions(UserId, IsActive);

-- Registro de auditoría de mensajes (no es el historial del LLM — ese vive
-- en el cliente). Se persiste para auditoría, analytics y trazabilidad.
CREATE TABLE MessageAuditLog (
    Id            BIGINT IDENTITY PRIMARY KEY,
    SessionId     UNIQUEIDENTIFIER NOT NULL
                  REFERENCES ConversationSessions(Id),
    Role          NVARCHAR(20) NOT NULL,  -- 'user' | 'assistant'
    ContentHash   NVARCHAR(64) NOT NULL,  -- SHA-256 del contenido (no el texto)
    TokensUsed    INT NULL,
    ToolsInvoked  NVARCHAR(MAX) NULL,     -- JSON array de tools llamadas
    CreatedAt     DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
CREATE INDEX IX_AuditLog_SessionId ON MessageAuditLog(SessionId);

CREATE TABLE RefreshTokens (
    Id         INT IDENTITY PRIMARY KEY,
    UserId     INT NOT NULL REFERENCES Users(Id),
    Token      NVARCHAR(512) NOT NULL UNIQUE,
    ExpiresAt  DATETIME2 NOT NULL,
    CreatedAt  DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    IsRevoked  BIT NOT NULL DEFAULT 0
);
```

### 3.2 Reglas de Integridad del Modelo

- Solo un `AgentPrompt` con `IsActive = 1` por `AgentId` en todo momento.
  Esta invariante se garantiza con un índice filtrado y se valida en la capa
  Application antes de activar una nueva versión.
- El campo `ContextRoute` de `Agents` es la clave de resolución del agente.
  Debe ser único y nunca cambiarse en producción sin una migración explícita.
- La tabla `MessageAuditLog` almacena el hash del contenido, no el texto en
  claro. El historial legible vive exclusivamente en `localStorage` del cliente.
- Las clases en `ToolClassName` deben existir en el ensamblado y registrarse
  en DI al arrancar. Si una clase no se encuentra, el sistema loggea error y
  deshabilita ese tool — nunca falla en startup.

---

## 4. Principios de Arquitectura

### 4.1 Separación Estricta de Responsabilidades

- El dominio (entidades, interfaces) no tiene dependencias de frameworks.
- La lógica de negocio (resolución de agente, construcción de contexto,
  despacho de tools) vive en la capa Application, no en los hubs ni endpoints.
- Los hubs SignalR y los endpoints HTTP son thin: reciben, delegan al use case,
  devuelven o transmiten el resultado.

### 4.2 Inversión de Dependencias

- Toda dependencia externa (DB, Claude API, tools, cache) se accede vía interfaz.
- Las implementaciones concretas se registran en el contenedor de DI.
- Nunca instanciar servicios con `new` fuera del contexto de DI o tests.

### 4.3 Aislamiento por Usuario

- Todo query a `ConversationSessions` y `MessageAuditLog` filtra por `UserId`
  extraído del JWT — nunca del cuerpo del mensaje SignalR.
- El `UserId` se extrae del claim en un método de extensión
  `HubCallerContext.GetUserId()` centralizado.
- Nunca confiar en un `userId` que venga del cliente en el payload.

### 4.4 Zero Hardcode de Comportamiento

- Ningún system prompt, instrucción de comportamiento, ni definición de tool
  puede estar como literal de string en el código fuente `.cs` o `.ts`.
- Los prompts son registros de `AgentPrompts` en SQL Server.
- Los tools son clases C# descubiertas por nombre desde `AgentTools` en SQL Server.
- Un cambio de comportamiento del agente es una operación de base de datos,
  no un redespliegue.

---

## 5. Acceso a Datos con Dapper — Reglas No Negociables

1. **Dapper es el único mecanismo de acceso a datos.** No se permite Entity
   Framework Core, NHibernate ni ningún ORM con migrations automáticas.

2. **Todo SQL es explícito y versionado.** Cada query vive como constante
   privada en su repositorio o en un archivo `.sql` dentro de
   `src/ConversationalAgent.Infrastructure/Sql/`. Nunca SQL inline en use cases.

3. **Migraciones con DbUp.** Scripts `.sql` numerados secuencialmente en
   `src/ConversationalAgent.Infrastructure/Migrations/`. Nunca modificar un
   script ya ejecutado — siempre crear uno nuevo.

4. **Parámetros nombrados siempre.** Todo query usa parámetros nombrados
   (`@Param`) — cero interpolación de strings en SQL.

5. **Transacciones explícitas.** Operaciones que afectan múltiples tablas usan
   `IDbTransaction` pasado explícitamente a Dapper.

6. **`IDbConnection` nunca se inyecta directamente.** Se inyecta
   `IDbConnectionFactory` que crea y cierra conexiones correctamente.

```csharp
// Patrón obligatorio de repositorio
public class SqlAgentRepository : IAgentRepository
{
    private readonly IDbConnectionFactory _factory;

    public async Task<AgentWithPrompt?> ResolveByRouteAsync(
        string contextRoute, CancellationToken ct)
    {
        using var conn = _factory.Create();
        return await conn.QuerySingleOrDefaultAsync<AgentWithPrompt>(
            AgentQueries.ResolveByRoute,
            new { ContextRoute = contextRoute }
        );
    }
}
```

---

## 6. Autenticación y Autorización — Reglas No Negociables

1. **JWT Bearer es el único mecanismo de autenticación.** Se usa
   `Microsoft.AspNetCore.Authentication.JwtBearer`. No se admiten cookies
   de sesión ni API keys fijas.

2. **SignalR autentica con JWT.** El token se pasa en el query string
   `?access_token=` para la negociación WebSocket. La validación es idéntica
   a la de los endpoints HTTP.

3. **Refresh tokens en base de datos.** Access token TTL: 15 minutos.
   Refresh token TTL: 7 días.

4. **Passwords con BCrypt.** Librería obligatoria: `BCrypt.Net-Next`.

5. **Roles: `User` y `Admin`.** Los endpoints de gestión de agentes, prompts
   y tools requieren rol `Admin`. Los hubs conversacionales requieren rol `User`
   o `Admin`.

6. **Configuración JWT en `appsettings.json` — nunca en código:**

```json
{
  "Jwt": {
    "SecretKey": "{{from-env-var-never-in-repo}}",
    "Issuer": "conversational-agent-api",
    "Audience": "conversational-agent-client",
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 7
  }
}
```

7. **El `SecretKey` NUNCA se commitea al repositorio.** Vive en variable de
   entorno `JWT__SecretKey` en local y en Azure Key Vault en producción.

7. **El `SecretKey` NUNCA se commitea al repositorio.** Vive en variable de
   entorno `JWT__SecretKey` en local y en Azure Key Vault en producción.

---

## 7. Caché de Prompts con Redis — Reglas No Negociables

### 7.1 Principio de Aislamiento

El resto del sistema no sabe que existe Redis. La única abstracción
visible para la capa Application es `IAgentCacheService`. Ninguna clase
fuera de Infrastructure referencia `IDistributedCache`, `StackExchange.Redis`
ni ningún tipo de la librería de Redis.

### 7.2 Interfaz Obligatoria

```csharp
// src/ConversationalAgent.Domain/Interfaces/IAgentCacheService.cs
public interface IAgentCacheService
{
    Task<AgentContext?> GetAsync(string contextRoute, CancellationToken ct);
    Task SetAsync(string contextRoute, AgentContext context, CancellationToken ct);
    Task InvalidateAsync(string contextRoute, CancellationToken ct);
}
```

### 7.3 Implementaciones

- **`RedisAgentCacheService`** (Infrastructure): implementación de producción.
  Usa `IDistributedCache` con proveedor Redis. TTL de 1 hora fijo.
  Serializa `AgentContext` con `System.Text.Json`. Se registra en DI
  cuando `Redis__Enabled = true`.

- **`InMemoryAgentCacheService`** (Infrastructure): implementación de
  desarrollo local sin Redis. Usa `ConcurrentDictionary`. Se registra
  en DI cuando `Redis__Enabled = false`. Nunca en producción.

### 7.4 Invalidación Obligatoria

Toda operación que cambie el prompt activo de un agente **debe** llamar
a `IAgentCacheService.InvalidateAsync(contextRoute)` como parte de la
misma operación de negocio — no como efecto secundario opcional. Esto
incluye `ActivatePromptVersion` y cualquier operación futura que modifique
el estado activo de un agente o sus tools habilitadas.

### 7.5 Configuración

```json
{
  "Redis": {
    "ConnectionString": "",
    "Enabled": true
  }
}
```

- En desarrollo local sin Docker: `"Enabled": false` en
  `appsettings.Development.json` — usa `InMemoryAgentCacheService`.
- En Docker y producción: `Redis__ConnectionString` desde variable de
  entorno, `Redis__Enabled = true`.
- La connection string de Redis NUNCA se commitea con valor real.

### 7.6 Docker Compose

Redis se agrega como servicio en `docker-compose.yml`:

```yaml
redis:
  image: redis:7-alpine
  ports: ["6379:6379"]

api:
  depends_on: [redis]
  environment:
    - Redis__ConnectionString=redis:6379
    - Redis__Enabled=true
```

---

## 8. Sistema de Tools — Reglas No Negociables

### 8.1 Interfaz Obligatoria

Todo tool implementa esta interfaz. No hay otra forma de crear un tool:

```csharp
// src/ConversationalAgent.Domain/Interfaces/ITool.cs
public interface ITool
{
    /// Nombre que Claude usará para invocar el tool (snake_case)
    string Name { get; }

    /// Descripción que se inyecta en el system prompt para que Claude
    /// sepa cuándo y cómo invocar el tool
    string Description { get; }

    /// Schema JSON de los parámetros de entrada (JSON Schema draft-07)
    JsonDocument InputSchema { get; }

    /// Ejecuta el tool y devuelve el resultado como string
    Task<string> ExecuteAsync(JsonDocument input, CancellationToken ct);
}
```

### 8.2 Registro y Descubrimiento

- Al arrancar, `ToolRegistry` escanea todos los tipos del ensamblado
  `Infrastructure` que implementan `ITool` y los registra en DI con nombre
  igual a su `FullName`.
- Cuando el `AgentResolver` carga un agente, consulta `AgentTools` en BD y
  resuelve las clases por `ToolClassName` desde el `ToolRegistry`.
- Si un `ToolClassName` no existe en el ensamblado, se loggea como `Warning`
  y ese tool se omite — el agente opera sin él, nunca falla en startup.

### 8.3 Convenciones de Implementación

- Cada tool vive en `src/ConversationalAgent.Infrastructure/Tools/`.
- Un tool por archivo. El nombre del archivo es `{NombrePascal}Tool.cs`.
- Los tools son stateless. Si necesitan estado, lo obtienen de sus dependencias
  inyectadas — nunca de campos mutables de instancia.
- Los tools que llaman a APIs externas usan `IHttpClientFactory` — nunca
  instancian `HttpClient` directamente.
- Todo tool incluye timeout y cancellation token propagado desde el hub.

```csharp
// Ejemplo de estructura de tool
// src/ConversationalAgent.Infrastructure/Tools/WeatherTool.cs
public sealed class WeatherTool : ITool
{
    private readonly IHttpClientFactory _http;

    public WeatherTool(IHttpClientFactory http) => _http = http;

    public string Name => "get_weather";
    public string Description => "..."; // nunca vacío
    public JsonDocument InputSchema => JsonDocument.Parse("""
        { "type": "object", "properties": { "city": { "type": "string" } },
          "required": ["city"] }
        """);

    public async Task<string> ExecuteAsync(
        JsonDocument input, CancellationToken ct)
    {
        // implementación...
    }
}
```

---

## 9. Integración con Claude (Anthropic) — Reglas No Negociables

### 9.1 Comunicación

- El backend llama a la API de Anthropic directamente con `HttpClient`
  configurado vía `IHttpClientFactory`. No se usa SDK de terceros.
- El endpoint es `https://api.anthropic.com/v1/messages`.
- La API key se inyecta desde variable de entorno — nunca hardcodeada.

### 9.2 Motor de Razonamiento: Ciclo ReAct

El agente opera bajo el patrón **ReAct (Reason + Act)**: Claude razona
sobre qué necesita, actúa invocando tools, observa los resultados, y repite
hasta tener suficiente información para responder al usuario. Este ciclo
es el corazón del sistema y debe estar modelado explícitamente en el código.

```
        ┌─────────────────────────────────────────────────────┐
        │                  CICLO ReAct                        │
        │                                                     │
        │  [mensaje usuario + historial + system prompt]      │
        │                    │                                │
        │                    ▼                                │
        │             Claude razona                           │
        │           (Thought interno)                         │
        │                    │                                │
        │         ┌──────────┴──────────┐                    │
        │         │                     │                     │
        │    tool_use?              respuesta                 │
        │    (Act)                  en texto                  │
        │         │                     │                     │
        │         ▼                     ▼                     │
        │   Ejecutar tool          StreamComplete             │
        │   (Observe)           ──► cliente                   │
        │         │                                           │
        │   tool_result                                       │
        │   reinyectado                                       │
        │   en historial                                      │
        │         │                                           │
        │         └──────► Claude razona de nuevo             │
        │                  (siguiente iteración)              │
        │                                                     │
        │   Máximo: Claude__MaxReActIterations (config)       │
        └─────────────────────────────────────────────────────┘
```

El ciclo se implementa íntegramente en el **`ReActOrchestrator`** en la
capa Application — no en el hub ni en el `ClaudeClient`. El hub solo
inicia el ciclo y transmite los tokens al cliente.

### 9.3 Tipos Discriminados de Respuesta del LLM

`ClaudeClient` no devuelve solo tokens de texto. Devuelve una unión
discriminada que el `ReActOrchestrator` interpreta en cada iteración:

```csharp
// src/ConversationalAgent.Domain/LLM/LLMEvent.cs
public abstract record LLMEvent;

/// Token de texto — se reenvía al cliente vía SignalR en tiempo real
public sealed record TextToken(string Value) : LLMEvent;

/// Claude quiere invocar una tool — el orquestador lo intercepta
public sealed record ToolUseRequested(
    string ToolCallId,   // id único del bloque tool_use de Anthropic
    string ToolName,
    JsonDocument Input
) : LLMEvent;

/// Claude terminó su turno completo (texto o tool_use procesado)
public sealed record TurnCompleted : LLMEvent;
```

### 9.4 Interfaz Obligatoria del LLM Client

```csharp
// src/ConversationalAgent.Domain/Interfaces/ILLMClient.cs
public interface ILLMClient
{
    /// Envía un turno al LLM y hace streaming de eventos discriminados.
    /// Un turno puede producir: TextToken*, (ToolUseRequested+, TurnCompleted)
    /// o solo TurnCompleted si Claude responde sin texto.
    IAsyncEnumerable<LLMEvent> StreamTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> history,
        IReadOnlyList<ITool> tools,
        CancellationToken ct);
}
```

### 9.5 Parseo del Stream SSE de Anthropic

La API de Anthropic devuelve Server-Sent Events (SSE) línea a línea.
`ClaudeClient` lee el stream HTTP **sin bufferizar la respuesta completa**
— procesa cada línea SSE en cuanto llega y emite el `LLMEvent` correspondiente
de forma inmediata. Esta es la regla que garantiza que el usuario vea
tokens en tiempo real.

#### Formato SSE de Anthropic

Cada evento tiene dos líneas: `event:` y `data:`. Una línea vacía separa
eventos. Los eventos relevantes para el parseo son:

```
event: content_block_start
data: {"type":"content_block_start","index":0,"content_block":{"type":"text"}}

event: content_block_delta
data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hola"}}

event: content_block_delta
data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":", ¿cómo"}}

event: content_block_start
data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_01","name":"get_weather","input":{}}}

event: content_block_delta
data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"city\":"}}

event: content_block_delta
data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"\"Madrid\"}"}}

event: content_block_stop
data: {"type":"content_block_stop","index":1}

event: message_stop
data: {"type":"message_stop"}
```

#### Reglas de Parseo Obligatorias

1. **`HttpCompletionOption.ResponseHeadersRead`** es obligatorio en la
   llamada a `HttpClient`. Nunca usar `GetStringAsync` ni ningún método
   que espere el body completo.

2. **Lectura línea a línea con `StreamReader`**. El loop lee una línea,
   la clasifica y actúa — nunca acumula el cuerpo entero antes de parsear.

3. **Mapeo de eventos SSE → `LLMEvent`**:

   | Evento SSE | Delta type | Acción |
   |---|---|---|
   | `content_block_start` | `type = "text"` | Iniciar bloque de texto (estado interno) |
   | `content_block_delta` | `text_delta` | Emitir `TextToken(delta.text)` inmediatamente |
   | `content_block_start` | `type = "tool_use"` | Registrar `tool_use` en curso (`id`, `name`) |
   | `content_block_delta` | `input_json_delta` | Acumular `partial_json` en buffer del tool |
   | `content_block_stop` | — | Si el bloque era `tool_use`: parsear buffer JSON acumulado y emitir `ToolUseRequested` |
   | `message_stop` | — | Emitir `TurnCompleted` |
   | `error` | — | Lanzar `ClaudeApiException` con el mensaje del evento |
   | Resto de eventos | — | Ignorar silenciosamente |

4. **Los `text_delta` se emiten sin ningún buffer adicional.** Cada delta
   llega con uno o más tokens — se reenvía tal cual al `ReActOrchestrator`
   que lo pasa al hub que lo pasa al cliente. Sin acumulación intermedia.

5. **El input JSON de un `tool_use` sí se acumula** en un `StringBuilder`
   hasta recibir `content_block_stop` de ese bloque, porque Anthropic lo
   fragmenta en múltiples `input_json_delta`. Solo entonces se parsea y se
   emite `ToolUseRequested`.

6. **Un turno puede contener múltiples bloques** (texto + tool_use, o solo
   texto, o solo tool_use). El parseo mantiene estado interno por `index`
   de bloque para manejar bloques concurrentes correctamente.

7. **Líneas `event:` sin `data:` asociado** se ignoran. Líneas que no
   empiezan con `event:` ni `data:` ni son vacías se ignoran silenciosamente.

#### Estructura interna del parser

```csharp
// src/ConversationalAgent.Infrastructure/LLM/ClaudeClient.cs
public sealed class ClaudeClient : ILLMClient
{
    public async IAsyncEnumerable<LLMEvent> StreamTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> history,
        IReadOnlyList<ITool> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var request = BuildRequest(systemPrompt, history, tools);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead, // OBLIGATORIO
            ct);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        // Estado del parser por bloque (índice → tipo)
        var blockTypes   = new Dictionary<int, string>();   // "text" | "tool_use"
        var toolIds      = new Dictionary<int, string>();   // index → tool_call_id
        var toolNames    = new Dictionary<int, string>();   // index → tool_name
        var toolBuffers  = new Dictionary<int, StringBuilder>(); // JSON parcial

        string? currentEvent = null;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);

            if (string.IsNullOrEmpty(line))
            {
                currentEvent = null;
                continue;
            }

            if (line.StartsWith("event:"))
            {
                currentEvent = line["event:".Length..].Trim();
                continue;
            }

            if (!line.StartsWith("data:")) continue;

            var json = line["data:".Length..].Trim();
            if (json == "[DONE]") break;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            switch (currentEvent)
            {
                case "content_block_start":
                    var idx   = root.GetProperty("index").GetInt32();
                    var block = root.GetProperty("content_block");
                    var bType = block.GetProperty("type").GetString()!;
                    blockTypes[idx] = bType;
                    if (bType == "tool_use")
                    {
                        toolIds[idx]    = block.GetProperty("id").GetString()!;
                        toolNames[idx]  = block.GetProperty("name").GetString()!;
                        toolBuffers[idx] = new StringBuilder();
                    }
                    break;

                case "content_block_delta":
                    var dIdx  = root.GetProperty("index").GetInt32();
                    var delta = root.GetProperty("delta");
                    var dType = delta.GetProperty("type").GetString();

                    if (dType == "text_delta")
                    {
                        var text = delta.GetProperty("text").GetString()!;
                        yield return new TextToken(text); // emitir inmediatamente
                    }
                    else if (dType == "input_json_delta")
                    {
                        var partial = delta.GetProperty("partial_json").GetString()!;
                        toolBuffers[dIdx].Append(partial); // acumular
                    }
                    break;

                case "content_block_stop":
                    var sIdx = root.GetProperty("index").GetInt32();
                    if (blockTypes.TryGetValue(sIdx, out var stoppedType)
                        && stoppedType == "tool_use")
                    {
                        var inputJson = toolBuffers[sIdx].ToString();
                        yield return new ToolUseRequested(
                            toolIds[sIdx],
                            toolNames[sIdx],
                            JsonDocument.Parse(inputJson));
                    }
                    break;

                case "message_stop":
                    yield return new TurnCompleted();
                    yield break;

                case "error":
                    var errMsg = root
                        .GetProperty("error")
                        .GetProperty("message")
                        .GetString()!;
                    throw new ClaudeApiException(errMsg);
            }
        }
    }
}
```

### 9.6 ReActOrchestrator — Loop Obligatorio

```csharp
// src/ConversationalAgent.Application/Services/ReActOrchestrator.cs
public sealed class ReActOrchestrator
{
    // Límite de iteraciones para evitar loops infinitos
    private readonly int _maxIterations; // desde Claude__MaxReActIterations

    /// Ejecuta el ciclo ReAct completo para un mensaje del usuario.
    /// Emite TextToken al hub vía callback onToken.
    /// Retorna cuando Claude produce una respuesta final en texto.
    public async Task RunAsync(
        AgentContext context,              // systemPrompt + tools resueltos
        List<ConversationMessage> history, // historial mutable del ciclo
        Func<string, Task> onToken,        // callback → hub → SignalR
        CancellationToken ct)
    {
        var iteration = 0;

        while (iteration < _maxIterations)
        {
            iteration++;
            var toolCallsThisTurn = new List<ToolCallResult>();
            var hasText = false;

            await foreach (var evt in _llmClient.StreamTurnAsync(
                context.SystemPrompt, history, context.Tools, ct))
            {
                switch (evt)
                {
                    case TextToken t:
                        hasText = true;
                        await onToken(t.Value); // → hub → ReceiveToken
                        break;

                    case ToolUseRequested req:
                        // Ejecutar tool y acumular resultado
                        var result = await _toolDispatcher.ExecuteAsync(
                            req.ToolName, req.Input, ct);
                        toolCallsThisTurn.Add(
                            new ToolCallResult(req.ToolCallId, req.ToolName, result));
                        break;

                    case TurnCompleted:
                        break;
                }
            }

            if (toolCallsThisTurn.Count == 0)
            {
                // Claude respondió en texto — ciclo terminado
                return;
            }

            // Claude invocó tools — reinyectar resultados y continuar
            history.Add(ConversationMessage.AssistantToolUse(toolCallsThisTurn));
            history.Add(ConversationMessage.ToolResults(toolCallsThisTurn));
        }

        // Se alcanzó el límite de iteraciones — respuesta de seguridad
        await onToken("[El agente alcanzó el límite de razonamiento. " +
                      "Por favor reformula tu pregunta.]");
    }
}
```

**Reglas del orquestador:**
- El loop vive en `ReActOrchestrator` — nunca en el hub ni en `ClaudeClient`.
- `_maxIterations` viene de `Claude__MaxReActIterations` en config. Valor
  por defecto: `10`. Nunca hardcodeado.
- Si se alcanza el límite, se emite un mensaje de aviso al usuario —
  nunca excepción ni silencio.
- `OperationCanceledException` (cliente desconectado) se propaga sin envolver.
- Los `ToolUseRequested` de un mismo turno se ejecutan secuencialmente.
  Ejecución paralela de tools queda fuera del alcance del MVP.

### 9.7 Construcción del Historial ReAct

El historial que viaja a Claude sigue el formato de mensajes de Anthropic.
Los mensajes de tool use y tool result se modelan así:

```csharp
// src/ConversationalAgent.Domain/Models/ConversationMessage.cs
public sealed record ConversationMessage
{
    public string Role { get; init; }         // "user" | "assistant"
    public object Content { get; init; }      // string | ContentBlock[]

    /// Mensaje de texto simple (usuario o asistente)
    public static ConversationMessage Text(string role, string text) => ...

    /// Bloque assistant con tool_use (lo que Claude "pensó hacer")
    public static ConversationMessage AssistantToolUse(
        IEnumerable<ToolCallResult> calls) => ...

    /// Bloque user con tool_result (lo que el sistema observó)
    public static ConversationMessage ToolResults(
        IEnumerable<ToolCallResult> calls) => ...
}
```

El historial inicial viene del cliente (`localStorage`). Los mensajes de
tool use/result del ciclo ReAct se agregan **solo en memoria** durante
el procesamiento del turno — nunca se persisten en BD ni se devuelven
al cliente como parte del historial. El cliente solo recibe los tokens
de texto final.

### 9.8 Construcción del Payload

- El `systemPrompt` viene de `AgentPrompts.SystemPrompt` en BD y pasa
  por `IPromptInterpolator` antes de enviarse a Claude. Nunca se envía
  el texto crudo de BD directamente al LLM.
- El `history` inicial viene SIEMPRE del cliente (reconstruido desde
  `localStorage` y enviado en cada mensaje SignalR).
- Los `tools` se construyen a partir de los `ITool` resueltos para ese
  agente, mapeando `Name`, `Description` e `InputSchema` al formato de
  Anthropic.
- El historial se envía completo en cada turno. No hay truncado ni ventana
  deslizante — es responsabilidad del cliente no enviar historiales que
  excedan el context window del modelo.

### 9.9 Interpolación de Prompts — Reglas No Negociables

Los prompts almacenados en `AgentPrompts.SystemPrompt` pueden contener
variables y secciones condicionales. El `IPromptInterpolator` las resuelve
en tiempo de ejecución antes de enviar el prompt a Claude.

#### Sintaxis soportada

```
Variables simples:
  {{FechaActual}}       →  "martes, 26 de mayo de 2026"
  {{NombreUsuario}}     →  "Juan Pérez"
  {{EmailUsuario}}      →  "juan@ejemplo.com"
  {{ContextRoute}}      →  "/chat/ventas"
  {{NombreAgente}}      →  "Agente de Ventas"

Secciones condicionales:
  {{#if NombreUsuario}}Hola, {{NombreUsuario}}.{{/if}}
  {{#if EsUsuarioActivo}}Tu cuenta está activa.{{/if}}
```

#### Variables disponibles (Opción B)

| Variable | Fuente | Valor cuando no está disponible |
|---|---|---|
| `{{FechaActual}}` | Sistema | Siempre disponible |
| `{{HoraActual}}` | Sistema | Siempre disponible |
| `{{ContextRoute}}` | `ConversationRequest` | Siempre disponible |
| `{{NombreAgente}}` | `AgentContext` | Siempre disponible |
| `{{NombreUsuario}}` | Perfil del usuario en BD | Cadena vacía |
| `{{EmailUsuario}}` | Perfil del usuario en BD | Cadena vacía |
| `{{EsUsuarioActivo}}` | Perfil del usuario en BD | `false` |

#### Interfaz obligatoria

```csharp
// src/ConversationalAgent.Domain/Interfaces/IPromptInterpolator.cs
public interface IPromptInterpolator
{
    /// Resuelve todas las variables y secciones condicionales del template.
    /// Nunca lanza excepción — variables no reconocidas se reemplazan
    /// por cadena vacía y secciones condicionales con variable falsa
    /// se eliminan del resultado.
    string Interpolate(string template, PromptContext context);
}

// src/ConversationalAgent.Domain/Models/PromptContext.cs
public sealed record PromptContext
{
    public string ContextRoute   { get; init; } = "";
    public string NombreAgente   { get; init; } = "";
    public string NombreUsuario  { get; init; } = "";
    public string EmailUsuario   { get; init; } = "";
    public bool   EsUsuarioActivo { get; init; } = false;
    public DateTimeOffset FechaHora { get; init; } = DateTimeOffset.UtcNow;
}
```

#### Implementación

```csharp
// src/ConversationalAgent.Infrastructure/Prompts/PromptInterpolator.cs
public sealed class PromptInterpolator : IPromptInterpolator
{
    public string Interpolate(string template, PromptContext context)
    {
        // 1. Resolver secciones condicionales primero
        //    {{#if Variable}}...{{/if}} → incluir o eliminar bloque
        // 2. Resolver variables simples
        //    {{Variable}} → valor del contexto o cadena vacía
        // 3. Retornar resultado — sin lanzar excepciones
    }
}
```

**Reglas del interpolador:**
- Es stateless y síncrono — no hace llamadas a BD ni a servicios externos.
- Variables no reconocidas se reemplazan silenciosamente por `""` —
  nunca lanza excepción ni deja el placeholder en el resultado.
- Secciones `{{#if}}` cuya variable es falsa o vacía se eliminan
  completamente del resultado, incluyendo espacios y saltos de línea.
- La interpolación ocurre **después** de cargar el prompt de caché/BD
  y **antes** de construir el payload para Claude.
- `PromptContext` lo construye el `ConversationOrchestrator` con datos
  del `ConversationRequest` y del perfil del usuario cargado desde BD.

#### Posición en el flujo del orquestador

```
AgentResolver.ResolveAsync()  →  prompt crudo desde caché/BD
        │
        ▼
IUserRepository.GetProfileAsync(userId)  →  NombreUsuario, Email, etc.
        │
        ▼
IPromptInterpolator.Interpolate(promptCrudo, context)  →  prompt final
        │
        ▼
ReActOrchestrator.RunAsync(promptFinal, ...)
```

### 9.10 Configuración

```json
{
  "Claude": {
    "ApiKey": "{{from-env-var-never-in-repo}}",
    "Model": "claude-sonnet-4-20250514",
    "MaxTokens": 4096,
    "TimeoutSeconds": 60,
    "MaxRetries": 3,
    "MaxReActIterations": 10
  }
}
```

### 9.10 Resiliencia con Polly

El `ClaudeClient` implementa retry con backoff exponencial y circuit breaker.
Si la API de Anthropic no está disponible, el hub envía un mensaje de error
genérico al cliente vía SignalR — nunca lanza excepción no controlada.

---


## 10. Guardrails — Control de Entrada y Salida

### 10.1 Responsabilidad y Alcance

Los guardrails son una capa de seguridad que revisa el mensaje del usuario
(entrada) y los tokens que llegan de Claude (salida) antes de que lleguen
a su destino. Cuando un guardrail detecta una violación, **redirige** —
emite una respuesta predefinida al cliente sin llamar a Claude ni continuar
el flujo. El usuario recibe una respuesta coherente, no un error técnico.

Los guardrails son **globales** — aplican a todos los agentes por igual.
Su configuración vive en `appsettings.json` y se puede modificar sin tocar
el código ni redesplegar.

La detección es **basada en regex** — determinista, sin latencia adicional
ni costo de API.

### 10.2 Interfaz Obligatoria

```csharp
// src/ConversationalAgent.Domain/Interfaces/IGuardrailService.cs
public interface IGuardrailService
{
    /// Evalúa un texto contra las reglas configuradas.
    /// Retorna GuardrailResult.Pass si no hay violación.
    /// Retorna GuardrailResult.Redirect con la respuesta predefinida si hay violación.
    GuardrailResult Evaluate(string text, GuardrailTarget target);
}

public enum GuardrailTarget { Input, Output }

public abstract record GuardrailResult
{
    public sealed record Pass : GuardrailResult;
    public sealed record Redirect(string RedirectMessage) : GuardrailResult;
}
```

### 10.3 Configuración en appsettings.json

```json
{
  "Guardrails": {
    "Enabled": true,
    "Rules": [
      {
        "Name":            "BlockOffTopic",
        "Target":          "Input",
        "Pattern":         "(?i)(política|elecciones|partido|presidente)",
        "RedirectMessage": "Solo puedo ayudarte con temas relacionados al servicio."
      },
      {
        "Name":            "SanitizeCreditCard",
        "Target":          "Output",
        "Pattern":         "\\b(?:\\d[ -]?){13,16}\\b",
        "RedirectMessage": "La respuesta contenía información sensible y fue bloqueada."
      },
      {
        "Name":            "BlockProfanity",
        "Target":          "Input",
        "Pattern":         "(?i)(insulto1|insulto2)",
        "RedirectMessage": "Por favor mantén un tono respetuoso."
      }
    ]
  }
}
```

Cada regla tiene:
- `Name`: identificador único para logging.
- `Target`: `"Input"` (mensaje del usuario) o `"Output"` (tokens del LLM).
- `Pattern`: expresión regular. Se compila una vez al arrancar la API.
- `RedirectMessage`: texto que recibe el cliente cuando la regla dispara.

**Reglas de configuración:**
- `Guardrails__Enabled = false` desactiva toda la capa sin tocar reglas.
- Las regex se compilan al registrar `IGuardrailService` en DI (`Startup`),
  no en cada evaluación.
- El orden de evaluación es el orden del array — la primera regla que
  dispara gana. Las reglas siguientes no se evalúan.
- Los patrones son case-insensitive por convención (`(?i)` al inicio).

### 10.4 Posición en el Flujo

```
Usuario
    │
    ▼
[INPUT GUARDRAIL]  ← IGuardrailService.Evaluate(userMessage, Input)
    │
    ├── Redirect → emitir AgentStream(redirectMessage)
    │              + AgentDone("completed")
    │              → retornar sin llamar al orquestador
    │
    ▼ Pass
ConversationOrchestrator.RunAsync(...)
    │
    ▼
ReActOrchestrator / ClaudeClient (streaming token a token)
    │
    ▼ cada TextToken
[OUTPUT GUARDRAIL] ← IGuardrailService.Evaluate(tokenAcumulado, Output)
    │
    ├── Redirect → descartar tokens acumulados
    │              emitir AgentStream(redirectMessage)
    │              + AgentDone("completed")
    │              → cancelar stream
    │
    ▼ Pass
AgentStream(token) → cliente
```

**El guardrail de salida evalúa por frase, no por token individual.**
Los tokens se acumulan en un buffer hasta detectar un separador natural
(`.`, `?`, `!`, `\n`) y se evalúa el fragmento completo. Si pasa, los
tokens acumulados se liberan al cliente. Esto evita falsos positivos por
patrones que se fragmentan entre tokens.

### 10.5 Responsabilidad por Capa

- **Hub**: evalúa el guardrail de entrada antes de llamar al orquestador.
  Si redirige, emite `AgentStream(redirectMessage)` + `AgentDone("completed")`
  y retorna. No llama a `IConversationOrchestrator`.
- **ReActOrchestrator**: evalúa el guardrail de salida en cada fragmento
  acumulado antes de llamar `onToken`. Si redirige, llama
  `onToken(redirectMessage)` y lanza `GuardrailRedirectException` para
  interrumpir el stream. El hub la captura y emite `AgentDone("completed")`.
- `IGuardrailService` es **stateless** — no tiene memoria entre llamadas.
- `GuardrailService` vive en Infrastructure. `IGuardrailService` en Domain.

### 10.6 Logging de Guardrails

Cada vez que una regla dispara se loggea como `Warning` con:
- `RuleName`, `Target`, el hash SHA-256 del texto que disparó la regla
  (nunca el texto en claro), `UserId` hasheado, `ContextRoute`.

Nunca se loggea el contenido del mensaje ni la respuesta en claro.

---

## 11. ConversationOrchestrator y Hub SignalR — Reglas No Negociables

### 11.1 Contrato Inamovible del Canal SignalR

El contrato de eventos es la interfaz pública del sistema de tiempo real.
Está centralizado en una clase estática en Domain — un único lugar que
referencian el hub, los tests y el frontend.

```csharp
// src/ConversationalAgent.Domain/SignalR/ConversationHubEvents.cs
public static class ConversationHubEvents
{
    // Servidor → Cliente
    public const string AgentThinking  = "AgentThinking";  // el agente empezó a procesar
    public const string AgentStream    = "AgentStream";     // un token de texto
    public const string AgentToolCall  = "AgentToolCall";  // el agente está usando una tool
    public const string AgentDone      = "AgentDone";      // fin — SIEMPRE se emite

    // Cliente → Servidor
    public const string SendMessage    = "SendMessage";
}

/// Payload de AgentDone — siempre presente, incluso en error o cancelación
public sealed record AgentDonePayload(
    string Status,        // "completed" | "error" | "cancelled"
    string? ErrorMessage  // null si Status == "completed" o "cancelled"
);
```

**Regla inamovible**: `AgentDone` se emite **siempre**, en cualquier
camino de ejecución — éxito, error o cancelación. El cliente nunca puede
quedar en estado `isStreaming = true` indefinidamente. Esta garantía es
responsabilidad exclusiva del hub.

### 11.2 ConversationOrchestrator — El Director de Orquesta

`ConversationOrchestrator` conecta todas las piezas: busca el prompt del
agente, lo pasa al ciclo ReAct, y distribuye los tokens hacia el cliente.
No sabe nada de SignalR — trabaja con callbacks puros.

```csharp
// src/ConversationalAgent.Application/Services/ConversationOrchestrator.cs
public interface IConversationOrchestrator
{
    Task RunAsync(
        ConversationRequest request,
        Func<Task> onThinking,             // callback → AgentThinking
        Func<string, Task> onToken,        // callback → AgentStream
        Func<string, Task> onToolCall,     // callback → AgentToolCall
        CancellationToken ct);
    // AgentDone NO es callback del orquestador — es responsabilidad del hub
}

public sealed record ConversationRequest(
    string ContextRoute,
    string UserId,
    IReadOnlyList<ConversationMessage> History,
    string UserMessage
);
```

**Flujo interno obligatorio:**

```
ConversationOrchestrator.RunAsync(request)
        │
        ▼
1. onThinking()   ← avisa al cliente que el agente empezó
        │
        ▼
2. AgentResolver.ResolveAsync(contextRoute)
        │
        ├── AgentNotFoundException si no existe
        │
        ▼
3. IUserRepository.GetProfileAsync(userId)
        │
        ▼
4. IPromptInterpolator.Interpolate(promptCrudo, PromptContext)
        │   ← resuelve {{NombreUsuario}}, {{FechaActual}}, etc.
        │
        ▼
5. Construir historial mutable:
   history = [...request.History, ConversationMessage.Text("user", request.UserMessage)]
        │
        ▼
6. ReActOrchestrator.RunAsync(promptInterpolado, history, onToken, onToolCall, ct)
        │
        ▼
7. IMessageAuditRepository.InsertAsync(...)
```

**Reglas:**
- `onThinking` se llama como primer paso — antes de resolver el agente,
  para que el cliente muestre feedback inmediato.
- El perfil del usuario se carga en el paso 3 para construir el
  `PromptContext` — si el usuario no existe en BD, se usan valores vacíos
  sin lanzar excepción.
- La interpolación ocurre siempre — aunque el prompt no tenga variables,
  `IPromptInterpolator.Interpolate` es idempotente y retorna el texto sin
  cambios.
- `AgentDone` no es responsabilidad del orquestador — el hub lo emite
  siempre en su bloque `finally`.
- Si `AgentResolver` lanza `AgentNotFoundException`, la excepción sube
  al hub sin envolver.
- Si `ReActOrchestrator` lanza (distinto de `OperationCanceledException`),
  la excepción sube al hub sin envolver.
- `OperationCanceledException` se propaga sin envolver en todos los casos.
- Nunca referencia `ILLMClient`, `IToolDispatcher` ni nada de Infrastructure.

### 11.3 Contrato del Hub — Implementación

```csharp
// src/ConversationalAgent.Api/Hubs/ConversationHub.cs
[Authorize]
public class ConversationHub : Hub
{
    private readonly IConversationOrchestrator _orchestrator;

    public async Task SendMessage(SendMessageRequest request)
    {
        var userId = Context.UserIdentifier!;
        var ct     = Context.ConnectionAborted;

        try
        {
            await _orchestrator.RunAsync(
                new ConversationRequest(
                    request.ContextRoute,
                    userId,
                    request.History,
                    request.UserMessage),
                onThinking: () =>
                    Clients.Caller.SendAsync(
                        ConversationHubEvents.AgentThinking, ct),
                onToken: t =>
                    Clients.Caller.SendAsync(
                        ConversationHubEvents.AgentStream, t, ct),
                onToolCall: n =>
                    Clients.Caller.SendAsync(
                        ConversationHubEvents.AgentToolCall, n, ct),
                ct);

            // Camino feliz
            await Clients.Caller.SendAsync(
                ConversationHubEvents.AgentDone,
                new AgentDonePayload("completed", null),
                ct);
        }
        catch (OperationCanceledException)
        {
            // Cliente desconectado — intentar enviar AgentDone best-effort
            try
            {
                await Clients.Caller.SendAsync(
                    ConversationHubEvents.AgentDone,
                    new AgentDonePayload("cancelled", null));
            }
            catch { /* conexión ya cerrada — ignorar */ }
        }
        catch (AgentNotFoundException)
        {
            await Clients.Caller.SendAsync(
                ConversationHubEvents.AgentDone,
                new AgentDonePayload("error", "Agente no disponible."),
                ct);
        }
        catch (Exception)
        {
            await Clients.Caller.SendAsync(
                ConversationHubEvents.AgentDone,
                new AgentDonePayload("error", "Error procesando el mensaje."),
                ct);
        }
    }
}
```

**`AgentDone` se emite en los cuatro caminos posibles:**

| Camino | `Status` | `ErrorMessage` |
|---|---|---|
| Respuesta completa | `"completed"` | `null` |
| Agente no encontrado | `"error"` | `"Agente no disponible."` |
| Error interno / Claude | `"error"` | `"Error procesando el mensaje."` |
| Cliente desconectado | `"cancelled"` | `null` |

El mensaje de error en `AgentDone` es siempre genérico — nunca expone
detalles técnicos ni stack traces. El error real se loggea en el servidor.

### 11.4 Capas y Dependencias

```
Hub (Api)
  └── IConversationOrchestrator (Application)
        ├── IAgentResolver (Application)
        │     └── IAgentCacheService (Domain) ← Redis (Infrastructure)
        │     └── IAgentRepository (Domain)   ← SQL (Infrastructure)
        ├── IReActOrchestrator (Application)
        │     ├── ILLMClient (Domain)          ← ClaudeClient (Infrastructure)
        │     └── IToolDispatcher (Domain)     ← ToolDispatcher (Infrastructure)
        └── IMessageAuditRepository (Domain)   ← SQL (Infrastructure)
```

Ninguna flecha salta niveles. El hub no conoce Infrastructure.
Application no conoce SignalR, `ConversationHubEvents` ni `HttpContext`.

### 11.5 Autenticación del Hub

- El hub está decorado con `[Authorize]`.
- El JWT se transmite en query string `?access_token=` durante la negociación.
- El `UserId` se extrae de `Context.UserIdentifier` — nunca del payload.
- El `UserId` se pasa dentro del `ConversationRequest` — el orquestador
  no accede al contexto HTTP.

### 11.6 Manejo de Cancelación

- `Context.ConnectionAborted` es el `CancellationToken` que se pasa al
  orquestador en cada llamada.
- Si el cliente se desconecta, `OperationCanceledException` sube por toda
  la cadena. El hub intenta emitir `AgentDone("cancelled")` best-effort
  y no loggea la cancelación como error.

---


## 12. Minimal API — Convenciones Obligatorias

Sin Controllers. Solo Minimal API. Los endpoints HTTP gestionan auth y
administración; la conversación va por SignalR.

```csharp
var auth   = app.MapGroup("/api/auth");
var admin  = app.MapGroup("/api/admin").RequireAuthorization("AdminPolicy");
var agents = admin.MapGroup("/agents");
var prompts = admin.MapGroup("/prompts");
var tools  = admin.MapGroup("/tools");

// Auth
auth.MapPost("/register",        AuthEndpoints.Register);
auth.MapPost("/login",           AuthEndpoints.Login);
auth.MapPost("/refresh",         AuthEndpoints.Refresh);
auth.MapPost("/logout",          AuthEndpoints.Logout);

// Admin — Agentes
agents.MapGet("/",               AgentEndpoints.GetAll);
agents.MapGet("/{id}",           AgentEndpoints.GetById);
agents.MapPost("/",              AgentEndpoints.Create);
agents.MapPut("/{id}",           AgentEndpoints.Update);
agents.MapDelete("/{id}",        AgentEndpoints.Delete);

// Admin — Prompts
prompts.MapGet("/agent/{agentId}",        PromptEndpoints.GetByAgent);
prompts.MapPost("/agent/{agentId}",       PromptEndpoints.Create);
prompts.MapPut("/{id}/activate",          PromptEndpoints.Activate);

// Admin — Tools
tools.MapGet("/agent/{agentId}",          ToolEndpoints.GetByAgent);
tools.MapPost("/agent/{agentId}",         ToolEndpoints.Enable);
tools.MapDelete("/{id}",                  ToolEndpoints.Disable);
tools.MapGet("/available",                ToolEndpoints.GetAvailable);
                                          // Lista las clases ITool registradas en DI
```

Los handlers son métodos estáticos en clases `*Endpoints` dentro de
`src/ConversationalAgent.Api/Endpoints/`. Validación con FluentValidation.
Todos los endpoints tienen `.WithName()`, `.WithSummary()` y `.WithTags()`.

---

## 13. Frontend React + TypeScript — Reglas No Negociables

### 13.1 Estructura de Carpetas

```
src/
  api/           # Clientes HTTP tipados (authApi.ts, adminApi.ts)
  hub/           # signalRClient.ts — wrapper del cliente SignalR
  │              # hubEvents.ts    — constantes espejo de ConversationHubEvents
  components/
    ui/           # Re-exports de shadcn/ui — nunca modificar directamente
    chat/         # ChatWindow, MessageBubble, StreamingBubble,
    │             # ThinkingIndicator, ToolCallIndicator, ReconnectingBanner
    admin/        # AgentForm, PromptEditor, ToolList
    layout/       # AppShell, Sidebar, Header
  pages/          # ChatPage, AdminPage, LoginPage
  stores/         # Zustand: useAuthStore, useChatStore, useAdminStore
  hooks/          # useSignalR, useConversation, useAdmin
  types/          # Agent, Prompt, Tool, ConversationMessage (espejo del dominio)
  lib/            # axios instance, zod schemas
```

### 13.2 Historial de Conversación en el Cliente

El historial completo persiste en `localStorage` indexado por `sessionId`
(UUID v4 generado en el cliente).

```typescript
// src/types/conversation.ts
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

- Al enviar un mensaje al hub, se incluye el array `messages` completo.
- El cliente estima tokens (~4 chars/token). Si supera ~180.000 tokens,
  muestra advertencia y ofrece iniciar nueva sesión.
- El historial nunca se envía al servidor para persistencia en texto claro.
  Solo se loggea el hash SHA-256 en `MessageAuditLog`.
- El historial se persiste en `localStorage` **solo** cuando llega
  `AgentDone` con `status === "completed"`. Si llega `"error"` o
  `"cancelled"`, el último intercambio no se guarda.

### 13.3 Cliente SignalR — Conexión y Reconexión

```typescript
// src/hub/signalRClient.ts — patrón obligatorio
export function createSignalRConnection(token: string): HubConnection {
  return new HubConnectionBuilder()
    .withUrl('/hubs/conversation', {
      accessTokenFactory: () => token,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    // intentos: inmediato, 2s, 5s, 10s, 30s — luego desiste
    .configureLogging(LogLevel.Warning)
    .build();
}
```

**Ciclo de vida de la conexión:**
- La conexión se inicia al montar `ChatPage` y se destruye al desmontar.
- El `useSignalR` hook escucha los eventos de ciclo de vida de SignalR
  y actualiza el estado `connectionStatus` en `useChatStore`:
  `"connected"` | `"reconnecting"` | `"disconnected"`.

**Estados visuales obligatorios por `connectionStatus`:**

| Estado | Componente visible | Comportamiento del input |
|---|---|---|
| `"connected"` | Nada especial | Habilitado |
| `"reconnecting"` | `ReconnectingBanner` (amarillo, "Reconectando...") | Deshabilitado |
| `"disconnected"` | `ReconnectingBanner` (rojo, "Sin conexión. Recarga la página.") | Deshabilitado |

El input de texto **nunca** está habilitado mientras `isStreaming = true`
o `connectionStatus !== "connected"`. Esto previene mensajes duplicados
y estado corrupto.

### 13.4 Eventos SignalR — Contrato del Cliente

Los eventos se referencian siempre desde `src/hub/hubEvents.ts` —
nunca como strings literales en componentes o hooks.

```typescript
// src/hub/hubEvents.ts — espejo de ConversationHubEvents del backend
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

**Comportamiento obligatorio por evento:**

| Evento | Acción en el store | Efecto visual |
|---|---|---|
| `AgentThinking` | `isThinking = true`, `isStreaming = false` | `ThinkingIndicator` visible (puntos animados) |
| `AgentStream` | Acumular token en `streamingContent`, `isThinking = false`, `isStreaming = true` | Token aparece en `StreamingBubble` en tiempo real |
| `AgentToolCall` | `activeToolCall = toolName` | `ToolCallIndicator` visible ("Consultando...") |
| `AgentDone("completed")` | Finalizar mensaje, persistir en localStorage, limpiar estado | `StreamingBubble` → `MessageBubble` finalizado |
| `AgentDone("error")` | Mostrar `errorMessage` como burbuja de error, limpiar estado | Burbuja roja con mensaje genérico |
| `AgentDone("cancelled")` | Descartar tokens parciales, limpiar estado | Sin burbuja — como si no hubiera pasado |

`isThinking`, `isStreaming` y `activeToolCall` se limpian **siempre**
al recibir `AgentDone`, sin importar el status.

### 13.5 Renderizado Progresivo Word-by-Word

- `StreamingBubble` renderiza `streamingContent` del store directamente —
  cada token nuevo causa un re-render inmediato.
- El cursor parpadeante (animación CSS) es visible mientras
  `isStreaming = true`.
- `ThinkingIndicator` muestra tres puntos animados mientras
  `isThinking = true`. Desaparece en cuanto llega el primer `AgentStream`.
- `ToolCallIndicator` muestra el nombre de la tool activa mientras
  `activeToolCall !== null`. Ejemplo: "Consultando inventario...".
- Nunca se acumulan tokens en un buffer antes de renderizar —
  cada token se añade al estado y React re-renderiza.

### 13.6 Tipado

- **Cero `any`.** Prohibido en todo el frontend. Se usa `unknown` con
  type guard.
- Los tipos del dominio se definen en `src/types/` — nunca inline en
  componentes.
- Los schemas Zod validan todo dato que viene de la API antes de usarse
  en estado.

### 13.7 Estado

- **Zustand** para estado de UI, sesión del usuario e historial activo.
- **TanStack Query** para operaciones HTTP del panel admin.
- Nunca `useEffect` para fetching — siempre `useQuery` / `useMutation`.

### 13.8 Componentes shadcn/ui

- Los componentes de `src/components/ui/` son generados por shadcn CLI —
  nunca se editan directamente.
- Las customizaciones van en componentes wrapper en `src/components/chat/`
  y `admin/`.
- Tailwind se usa exclusivamente con clases utilitarias — nunca `style={{}}`
  inline salvo para valores dinámicos imposibles de lograr con clases.

---

## 14. Estructura de Proyecto

```
/
├── src/
│   ├── ConversationalAgent.Domain/
│   │   ├── Entities/          # Agent, AgentPrompt, AgentTool, User, Session
│   │   ├── Interfaces/        # IAgentRepository, ILLMClient, ITool,
│   │   │                      # IToolRegistry, IToolDispatcher,
│   │   │                      # IAgentCacheService, IMessageAuditRepository,
│   │   │                      # IPromptInterpolator, IUserRepository,
│   │   │                      # IGuardrailService
│   │   ├── LLM/               # LLMEvent, TextToken, ToolUseRequested, TurnCompleted
│   │   ├── Models/            # ConversationMessage, AgentContext, ConversationRequest,
│   │   │                      # PromptContext
│   │   ├── SignalR/           # ConversationHubEvents, AgentDonePayload
│   │   └── Exceptions/        # AgentNotFoundException, ToolExecutionException
│   │
│   ├── ConversationalAgent.Application/
│   │   ├── UseCases/          # ActivatePromptVersion, GetAvailableTools
│   │   └── Services/          # ConversationOrchestrator   ← director de orquesta
│   │                          # AgentResolver              ← resuelve agente + caché
│   │                          # ReActOrchestrator          ← loop ReAct
│   │
│   ├── ConversationalAgent.Infrastructure/
│   │   ├── Data/              # SqlAgentRepository, SqlPromptRepository,
│   │   │                      # SqlToolRepository, SqlMessageAuditRepository,
│   │   │                      # SqlUserRepository
│   │   ├── Cache/             # RedisAgentCacheService, InMemoryAgentCacheService
│   │   ├── Prompts/           # PromptInterpolator
│   │   ├── Guardrails/        # GuardrailService
│   │   ├── Sql/               # AgentQueries.cs, PromptQueries.cs (constantes SQL)
│   │   ├── Migrations/        # 001_initial_schema.sql, 002_seed_admin.sql
│   │   ├── Auth/              # JwtService, PasswordHasher
│   │   ├── LLM/               # ClaudeClient, AnthropicPayloadBuilder
│   │   └── Tools/             # WeatherTool.cs, (demás tools aquí)
│   │
│   └── ConversationalAgent.Api/
│       ├── Endpoints/         # AuthEndpoints, AgentEndpoints,
│       │                      # PromptEndpoints, ToolEndpoints
│       ├── Hubs/              # ConversationHub.cs  (thin — solo delega)
│       └── Program.cs         # Composición raíz, DI, Middleware, SignalR
│
├── frontend/
│   ├── src/                   # Estructura detallada en sección 11.1
│   ├── index.html
│   ├── vite.config.ts
│   ├── tailwind.config.ts
│   └── tsconfig.json
│
├── tests/
│   ├── ConversationalAgent.Domain.Tests/
│   ├── ConversationalAgent.Application.Tests/
│   ├── ConversationalAgent.Infrastructure.Tests/  # Testcontainers (SQL Server real)
│   └── ConversationalAgent.Api.Tests/             # TestServer + SignalR test client
│
├── docker-compose.yml
├── docker-compose.override.yml
└── .env.example
```

Máximo 4 proyectos en `src/`. Proyectos adicionales requieren justificación.

---

## 15. Docker y Despliegue — Reglas No Negociables

### 13.1 Docker Compose Local

El SQL Server de desarrollo es la instancia instalada en la máquina host —
no se levanta un contenedor de base de datos. Los contenedores de API y
frontend se conectan al SQL Server del host usando la dirección especial
`host.docker.internal`.

```yaml
services:
  api:
    build: ./src
    ports: ["5000:8080"]
    environment:
      - ConnectionStrings__DefaultConnection=Server=host.docker.internal;Database=ConvAgent;User Id=${DB_USER};Password=${DB_PASSWORD};TrustServerCertificate=True;
      - Claude__ApiKey=${CLAUDE_API_KEY}
      - Jwt__SecretKey=${JWT_SECRET_KEY}
    extra_hosts:
      - "host.docker.internal:host-gateway"  # Linux: necesario para resolver el host

  frontend:
    build: ./frontend
    ports: ["3000:80"]
    depends_on: [api]
```

- En desarrollo local (fuera de Docker), la connection string va en
  `appsettings.Development.json` apuntando a `localhost` — nunca en
  `appsettings.json` base ni en el repositorio con credenciales reales:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=ConvAgent;User Id=sa;Password=...;TrustServerCertificate=True;"
  }
}
```

  Cuando la API corre dentro de Docker, la misma variable se sobreescribe
  con `host.docker.internal` vía `docker-compose.yml`.

### 13.2 Dockerfiles

- **API**: Imagen base `mcr.microsoft.com/dotnet/aspnet:9.0`. Build en
  `mcr.microsoft.com/dotnet/sdk:9.0`. Multi-stage obligatorio.
- **Frontend**: Build en `node:20-alpine`, servir con `nginx:alpine`.
  El `nginx.conf` incluye la regla de fallback a `index.html` para SPA y
  el proxy de WebSocket hacia el backend para SignalR.

### 13.3 Variables de Entorno

Estas variables DEBEN estar en `.env.example` documentadas y NUNCA en el
repositorio con valores reales:

```
CLAUDE_API_KEY=sk-ant-...
JWT_SECRET_KEY=...
DB_USER=sa
DB_PASSWORD=...
VITE_API_BASE_URL=http://localhost:5000
VITE_SIGNALR_HUB_URL=http://localhost:5000/hubs/conversation
```

### 13.4 Azure (Post-MVP)

- **API**: Azure App Service (Linux, .NET 9) con WebSocket habilitado
  (requisito para SignalR).
- **Frontend**: Azure Static Web Apps.
- **Base de datos**: Azure SQL Database.
- **Secretos**: Azure Key Vault + Managed Identity — nunca variables de
  entorno con valores reales en Azure Portal directamente.
- La cadena de conexión y el API key de Claude se obtienen de Key Vault
  en startup via `Azure.Extensions.AspNetCore.Configuration.Secrets`.
- **SignalR en Azure**: Para escalar horizontalmente, usar Azure SignalR
  Service en lugar de SignalR in-process. La configuración se activa con
  `AddAzureSignalR()` en `Program.cs` condicionada por variable de entorno.

---

## 16. Testing — Reglas No Negociables

1. **TDD**: Tests antes que implementación.
2. **Cobertura mínima del 80%** en Domain y Application.
3. **Tests de integración con Testcontainers** para SQL Server real —
   nunca mocks de base de datos en tests de repositorio.
4. **Tests del `ConversationHub`** usan el cliente SignalR de prueba de
   ASP.NET Core (`TestServer` + conexión SignalR real).
5. **Tests del `ClaudeClient`** usan `WireMock.Net` para simular el servidor
   Anthropic — nunca depender de la API real en tests.
6. **Tests del `ToolRegistry`** validan que todas las clases `ITool`
   registradas en DI tienen `Name` único, `Description` no vacía e
   `InputSchema` válido según JSON Schema.
7. **Tests del `AgentResolver`** cubren: ruta existente, ruta inexistente,
   agente sin prompt activo, agente con tools inválidas.
8. **Tests de tools** usan `WireMock.Net` para las APIs externas que invocan.

---

## 17. Seguridad y Observabilidad

- Nunca loggear el API Key de Claude ni el JWT SecretKey.
- Nunca loggear el contenido del system prompt en producción.
- Nunca loggear el contenido de los mensajes del usuario en producción —
  solo el hash SHA-256 en `MessageAuditLog`.
- Los errores de la API de Anthropic nunca se exponen raw al cliente —
  siempre se mapean a mensajes genéricos vía `ReceiveError` en SignalR.
- Los errores de ejecución de tools nunca se exponen al cliente —
  solo se loggea en servidor y se reinyecta un `tool_result` con mensaje genérico.
- Toda llamada al LLM incluye el `UserId` (hasheado) en los metadatos de
  trace de OpenTelemetry — sin PII en el valor.
- El tiempo de respuesta del LLM y el número de tokens se registran como
  métricas (`llm_latency_ms`, `llm_tokens_used`).
- Los WebSockets se sirven solo sobre TLS en producción.

---

## 18. Lo que el Agente de IA NO debe hacer

- ❌ NO usar Entity Framework Core, NHibernate ni ORM con migrations automáticas
- ❌ NO usar Controllers — solo Minimal API + Hubs SignalR
- ❌ NO hardcodear el API Key de Claude ni el JWT SecretKey en código
- ❌ NO hardcodear ningún system prompt como literal de string en archivos `.cs`
- ❌ NO hardcodear ninguna instrucción de comportamiento del agente en código fuente
- ❌ NO usar `.Result` o `.Wait()` en código async
- ❌ NO interpolar variables directamente en strings SQL
- ❌ NO inyectar `IDbConnection` directamente — siempre via factory
- ❌ NO crear más de 4 proyectos en `src/` sin justificación
- ❌ NO omitir `CancellationToken` en métodos que llaman a Claude o a tools
- ❌ NO confiar en `UserId` que venga del payload SignalR — siempre del JWT
- ❌ NO devolver errores de Claude/Anthropic, SQL o tools raw al cliente
- ❌ NO exponer bloques internos `tool_use`/`tool_result` de Claude al cliente
- ❌ NO implementar el loop ReAct en el hub ni en `ClaudeClient` — solo en `ReActOrchestrator`
- ❌ NO hardcodear el límite de iteraciones ReAct — siempre desde `Claude__MaxReActIterations`
- ❌ NO ejecutar tools en paralelo dentro de un mismo turno ReAct (fuera del alcance del MVP)
- ❌ NO persistir los mensajes intermedios de tool_use/tool_result en BD ni devolverlos al cliente
- ❌ NO usar `GetStringAsync`, `ReadAsStringAsync` ni ningún método que espere el body completo de la respuesta de Claude — siempre `HttpCompletionOption.ResponseHeadersRead`
- ❌ NO bufferizar tokens de texto antes de emitirlos — cada `text_delta` se convierte en `TextToken` y se reenvía inmediatamente
- ❌ NO emitir `ToolUseRequested` antes de recibir `content_block_stop` del bloque — el input JSON debe estar completo
- ❌ NO usar `any` en TypeScript — cero excepciones
- ❌ NO usar `useEffect` para fetching en el frontend
- ❌ NO editar directamente los componentes generados por shadcn CLI
- ❌ NO commitear archivos `.env` con valores reales al repositorio
- ❌ NO escribir implementación antes que los tests
- ❌ NO usar `style={{}}` inline en React cuando una clase Tailwind lo resuelve
- ❌ NO instanciar `HttpClient` directamente en tools — siempre `IHttpClientFactory`
- ❌ NO permitir que el frontend envíe historial en texto claro al servidor para
     persistencia — solo hash en auditoría
- ❌ NO enviar el mensaje del usuario al orquestador sin pasar primero por el guardrail de entrada
- ❌ NO evaluar guardrails de salida token por token — siempre por fragmento hasta separador natural
- ❌ NO compilar las regex de guardrails en cada evaluación — compilar una vez en DI al arrancar
- ❌ NO loggear el contenido del mensaje ni la respuesta en claro — solo hash SHA-256
- ❌ NO lanzar excepción técnica al cliente cuando un guardrail redirige — siempre emitir `AgentDone("completed")` con el mensaje de redirección
- ❌ NO lanzar excepción desde `IPromptInterpolator` por variables no reconocidas — siempre reemplazar por cadena vacía
- ❌ NO dejar placeholders `{{Variable}}` sin resolver en el prompt final que recibe Claude
- ❌ NO hacer llamadas a BD ni a servicios externos desde `IPromptInterpolator` — es stateless y síncrono
- ❌ NO omitir `AgentDone` en ningún camino de ejecución del hub — éxito, error y cancelación deben emitirlo
- ❌ NO emitir detalles técnicos de error en el payload de `AgentDone` — solo mensajes genéricos
- ❌ NO hacer que `ConversationOrchestrator` emita `AgentDone` — es responsabilidad exclusiva del hub
- ❌ NO acceder al contexto SignalR (`Context`, `Clients`) desde `ConversationOrchestrator` ni desde ninguna clase de Application
- ❌ NO referenciar `IDistributedCache`, `StackExchange.Redis` ni tipos de Redis fuera de Infrastructure
- ❌ NO omitir la invalidación de caché al activar una nueva versión de prompt
- ❌ NO usar `InMemoryAgentCacheService` en producción — solo en desarrollo local
- ❌ NO habilitar WebSocket sin TLS en producción
- ❌ NO habilitar el input de texto mientras `isStreaming = true` o `connectionStatus !== "connected"`
- ❌ NO acumular tokens en buffer antes de renderizar — cada `AgentStream` causa re-render inmediato
- ❌ NO usar strings literales para nombres de eventos SignalR en el frontend — siempre constantes de `hubEvents.ts`
- ❌ NO persistir el historial en `localStorage` si `AgentDone.status !== "completed"`
- ❌ NO dejar `isThinking`, `isStreaming` o `activeToolCall` en estado sucio — `AgentDone` los limpia siempre
- ❌ NO mostrar errores técnicos de conexión al usuario — solo el estado visual de `ReconnectingBanner`
