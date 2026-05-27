using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public Mock<IAuthService> AuthServiceMock { get; } = new();
    public Mock<ITransactionService> TransactionServiceMock { get; } = new();
    public Mock<IReportService> ReportServiceMock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var testConfig = new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "this-is-a-super-secret-test-jwt-key-123456",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
                ["Jwt:ExpiryDays"] = "7",
                ["Cors:AllowedOrigins:0"] = "http://localhost:4200"
            };

            config.AddInMemoryCollection(testConfig);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuthService>();
            services.RemoveAll<ITransactionService>();
            services.RemoveAll<IReportService>();

            services.AddSingleton(AuthServiceMock.Object);
            services.AddSingleton(TransactionServiceMock.Object);
            services.AddSingleton(ReportServiceMock.Object);

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName,
                options => { });
        });
    }
}