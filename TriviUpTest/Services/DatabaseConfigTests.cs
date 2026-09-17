using TriviUpBackend.Infrastructure;

namespace TriviUpTest.Services;

public class DatabaseConfigTests
{
    [Theory]
    [InlineData("postgresql://user:pass@host:5432/db")]
    [InlineData("postgres://user:pass@host:5432/db")]
    [InlineData("POSTGRES://user:pass@host:5432/db")]
    public void ConvertPostgresUriToNpgsql_SupportsBothSchemes(string uri)
    {
        var result = DatabaseConfig.ConvertPostgresUriToNpgsql(uri);

        Assert.Contains("Host=host", result);
        Assert.Contains("Port=5432", result);
        Assert.Contains("Database=db", result);
        Assert.Contains("Username=user", result);
        Assert.Contains("Password=pass", result);
        Assert.DoesNotContain("postgres://", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertPostgresUriToNpgsql_ParsesSslModeQuery()
    {
        var result = DatabaseConfig.ConvertPostgresUriToNpgsql(
            "postgres://u:p@proxy.rlwy.net:12345/railway?sslmode=require");

        Assert.Contains("Host=proxy.rlwy.net", result);
        Assert.Contains("SSL Mode=Require", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertPostgresUriToNpgsql_LeavesNativeNpgsqlUnchanged()
    {
        const string native = "Host=localhost;Port=5432;Database=triviup;Username=u;Password=p";
        Assert.Equal(native, DatabaseConfig.ConvertPostgresUriToNpgsql(native));
    }

    [Fact]
    public void ConvertPostgresUriToNpgsql_DecodesUrlEncodedPassword()
    {
        var result = DatabaseConfig.ConvertPostgresUriToNpgsql(
            "postgres://user:p%40ss%3Aword@host:5432/db");

        Assert.Contains("Password=p@ss:word", result);
    }
}
