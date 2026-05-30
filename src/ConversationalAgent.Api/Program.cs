using System.Text.Json;
using ConversationalAgent.Infrastructure.Data;
using DbUp;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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

EnsureDatabase.For.SqlDatabase(connectionString);

var upgrader = DeployChanges.To
    .SqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(
        typeof(SqlConnectionFactory).Assembly,
        script => script.Contains("Migrations"))
    .WithVariablesDisabled()
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
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(
            JsonSerializer.Serialize(new { status = report.Status.ToString() }));
    }
});

// ── (SPEC-02 agregará endpoints de auth aquí) ────────────────────
// ── (SPEC-05 agregará MapHub<ConversationHub> aquí) ──────────────

app.Run();

// Necesario para TestServer en Api.Tests
public partial class Program { }
