using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<ILogger<AuthService>> _loggerMock;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _configMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<AuthService>>();

        // Setup JWT config
        _configMock.Setup(c => c["Jwt:Key"]).Returns("super-secret-key-for-testing-must-be-32chars");
        _configMock.Setup(c => c["Jwt:Issuer"]).Returns("TestIssuer");
        _configMock.Setup(c => c["Jwt:Audience"]).Returns("TestAudience");
        _configMock.Setup(c => c.GetValue<int>("Jwt:ExpiryDays", 7)).Returns(7);

        _authService = new AuthService(_userRepoMock.Object, _configMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task RegisterAsync_WhenEmailTaken_ReturnsNull()
    {
        // Arrange
        _userRepoMock.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        var dto = new RegisterDto { Email = "test@test.com", Password = "password123" };

        // Act
        var result = await _authService.RegisterAsync(dto);

        // Assert
        result.Should().BeNull();
        _userRepoMock.Verify(r => r.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WhenEmailAvailable_CreatesUserAndReturnsToken()
    {
        // Arrange
        var newUser = new User { Id = 1, Email = "test@test.com", PasswordHash = "hashed" };
        _userRepoMock.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _userRepoMock.Setup(r => r.CreateAsync(It.IsAny<User>())).ReturnsAsync(newUser);

        var dto = new RegisterDto { Email = "TEST@TEST.COM", Password = "password123" };

        // Act
        var result = await _authService.RegisterAsync(dto);

        // Assert
        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrEmpty();
        result.Email.Should().Be("test@test.com"); // normalized
        result.UserId.Should().Be(1);
    }

    [Fact]
    public async Task LoginAsync_WhenUserNotFound_ReturnsNull()
    {
        // Arrange
        _userRepoMock.Setup(r => r.GetByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

        // Act
        var result = await _authService.LoginAsync(new LoginDto { Email = "x@x.com", Password = "pw" });

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WhenPasswordWrong_ReturnsNull()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Email = "test@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correctpassword")
        };
        _userRepoMock.Setup(r => r.GetByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);

        // Act
        var result = await _authService.LoginAsync(new LoginDto
        {
            Email = "test@test.com",
            Password = "wrongpassword"
        });

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsToken()
    {
        // Arrange
        var password = "correctpassword";
        var user = new User
        {
            Id = 42,
            Email = "test@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        };
        _userRepoMock.Setup(r => r.GetByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);

        // Act
        var result = await _authService.LoginAsync(new LoginDto
        {
            Email = "test@test.com",
            Password = password
        });

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().Be(42);
        result.Token.Should().NotBeNullOrEmpty();
    }
}