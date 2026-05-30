using ConversationalAgent.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;

namespace ConversationalAgent.Infrastructure.Tests.Data;

public class SqlConnectionFactoryTests
{
    // ── helpers ──────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(string? connectionString)
    {
        var sectionMock = new Mock<IConfigurationSection>();
        sectionMock.Setup(s => s["DefaultConnection"]).Returns(connectionString);

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c.GetSection("ConnectionStrings")).Returns(sectionMock.Object);

        return configMock.Object;
    }

    // ── tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithConnectionStringConfigured_DoesNotThrow()
    {
        var config = BuildConfig("Server=localhost;Database=Test;");

        var act = () => new SqlConnectionFactory(config);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithoutConnectionString_ThrowsInvalidOperationException()
    {
        var config = BuildConfig(null);

        var act = () => new SqlConnectionFactory(config);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_WithoutConnectionString_ExceptionMentionsConfigKey()
    {
        var config = BuildConfig(null);

        var act = () => new SqlConnectionFactory(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:DefaultConnection*");
    }
}
