using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

[TestFixture]
public class AuthServiceTests
{
    private Mock<IUserRepository> _userRepoMock;
    private Mock<IConfiguration> _configMock;
    private Mock<ILogger<AuthService>> _loggerMock;
    private AuthService _authService;

    [SetUp]
    public void Setup()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _configMock   = new Mock<IConfiguration>();
        _loggerMock   = new Mock<ILogger<AuthService>>();
        _configMock.Setup(c => c["Jwt:Key"]).Returns("super-secret-key-for-testing-must-be-32chars");
        _configMock.Setup(c => c["Jwt:Issuer"]).Returns("TestIssuer");
        _configMock.Setup(c => c["Jwt:Audience"]).Returns("TestAudience");
        _configMock.Setup(c => c.GetValue<int>("Jwt:ExpiryDays", 7)).Returns(7);
        _authService = new AuthService(_userRepoMock.Object, _configMock.Object, _loggerMock.Object);
    }

    [Test]
    public async Task RegisterAsync_WhenEmailTaken_ReturnsNull()
    {
        _userRepoMock.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        AuthResponseDto? result = await _authService.RegisterAsync(
            new RegisterDto { Email = "test@test.com", Password = "password123" });
        result.Should().BeNull();
        _userRepoMock.Verify(r => r.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Test]
    public async Task RegisterAsync_WhenEmailAvailable_CreatesUserAndReturnsToken()
    {
        User newUser = new User { Id = 1, Email = "test@test.com", PasswordHash = "hashed" };
        _userRepoMock.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _userRepoMock.Setup(r => r.CreateAsync(It.IsAny<User>())).ReturnsAsync(newUser);
        AuthResponseDto? result = await _authService.RegisterAsync(
            new RegisterDto { Email = "TEST@TEST.COM", Password = "password123" });
        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrEmpty();
        result.Email.Should().Be("test@test.com");
        result.UserId.Should().Be(1);
    }

    [TestCase("x@x.com", "anypass")]
    public async Task LoginAsync_WhenUserNotFound_ReturnsNull(string email, string password)
    {
        _userRepoMock.Setup(r => r.GetByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        AuthResponseDto? result = await _authService.LoginAsync(new LoginDto { Email = email, Password = password });
        result.Should().BeNull();
    }
}