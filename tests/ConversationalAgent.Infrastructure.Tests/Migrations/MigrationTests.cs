using ConversationalAgent.Infrastructure.Data;
using Dapper;
using DbUp;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace ConversationalAgent.Infrastructure.Tests.Migrations;

public sealed class MsSqlFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

public sealed class MigrationTests : IClassFixture<MsSqlFixture>
{
    private readonly string _connectionString;

    public MigrationTests(MsSqlFixture fixture)
    {
        _connectionString = fixture.ConnectionString;
        RunMigrations();
    }

    private void RunMigrations()
    {
        var upgrader = DeployChanges.To
            .SqlDatabase(_connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(SqlConnectionFactory).Assembly,
                script => script.Contains("Migrations"))
            .WithVariablesDisabled()
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        result.Successful.Should().BeTrue(because: result.Error?.Message ?? "migration failed");
    }

    // ── schema ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Migration_EmptyDatabase_RunsSuccessfully()
    {
        using var conn = new SqlConnection(_connectionString);
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM SchemaVersions");
        count.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("Users")]
    [InlineData("Agents")]
    [InlineData("AgentPrompts")]
    [InlineData("AgentTools")]
    [InlineData("ConversationSessions")]
    [InlineData("MessageAuditLog")]
    [InlineData("RefreshTokens")]
    public async Task Migration_AllSevenTables_Exist(string tableName)
    {
        using var conn = new SqlConnection(_connectionString);
        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName",
            new { tableName });
        exists.Should().Be(1, because: $"table {tableName} must exist after migration");
    }

    [Fact]
    public async Task Migration_AgentPrompts_HasUniqueVersionConstraint()
    {
        using var conn = new SqlConnection(_connectionString);
        var exists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.indexes
            WHERE name = 'UQ_AgentPrompt_Version'
              AND object_id = OBJECT_ID('AgentPrompts')
            """);
        exists.Should().Be(1, because: "UQ_AgentPrompt_Version unique constraint must exist");
    }

    [Fact]
    public async Task Migration_AgentPrompts_FilteredIndexPreventsMultipleActivePrompts()
    {
        using var conn = new SqlConnection(_connectionString);

        // seed already created one active prompt for the test agent; insert a second active one for the same agent
        var act = async () => await conn.ExecuteAsync(
            """
            INSERT INTO AgentPrompts (AgentId, Version, SystemPrompt, IsActive, CreatedBy)
            SELECT a.Id, 999, 'duplicate active', 1, u.Id
            FROM   Agents a
            CROSS JOIN Users u
            WHERE  a.ContextRoute = '/chat/test'
            AND    u.Email        = 'admin@convagent.local'
            """);

        await act.Should().ThrowAsync<SqlException>(
            because: "filtered unique index IX_AgentPrompts_AgentId_Active must block a second active prompt for the same agent");
    }

    // ── seed data ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Migration_SeedData_AdminUserExists()
    {
        using var conn = new SqlConnection(_connectionString);
        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Users WHERE Email = 'admin@convagent.local' AND Role = 'Admin'");
        exists.Should().Be(1, because: "seed must create the admin user");
    }

    [Fact]
    public async Task Migration_SeedData_TestAgentExists()
    {
        using var conn = new SqlConnection(_connectionString);
        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Agents WHERE Name = 'Agente de Prueba' AND ContextRoute = '/chat/test'");
        exists.Should().Be(1, because: "seed must create the test agent");
    }

    [Fact]
    public async Task Migration_SeedData_ExactlyOneActiveAgentPrompt()
    {
        using var conn = new SqlConnection(_connectionString);
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AgentPrompts WHERE IsActive = 1");
        count.Should().Be(1, because: "exactly one active prompt must exist after seed");
    }

    [Fact]
    public async Task Migration_RunTwice_IsIdempotent()
    {
        // run a second time — DbUp should skip already-applied scripts
        var act = () => RunMigrations();
        act.Should().NotThrow(because: "DbUp migrations must be idempotent");

        using var conn = new SqlConnection(_connectionString);
        var adminCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Users WHERE Email = 'admin@convagent.local'");
        adminCount.Should().Be(1, because: "idempotent seed must not duplicate the admin user");
    }
}
