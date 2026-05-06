public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _repoMock;
    private readonly Mock<IConfiguration>  _configMock;
    private readonly AuthService           _sut;

    public AuthServiceTests()
    {
        _repoMock   = new Mock<IUserRepository>();
        _configMock = new Mock<IConfiguration>();

        _configMock.Setup(c => c["Jwt:Key"])
            .Returns("super-secret-key-that-is-long-enough-for-hmac256");
        _configMock.Setup(c => c["Jwt:Issuer"])   .Returns("TestIssuer");
        _configMock.Setup(c => c["Jwt:Audience"]) .Returns("TestAudience");

        _sut = new AuthService(_repoMock.Object, _configMock.Object,
            Mock.Of<ILogger<AuthService>>());
    }

    [Fact]
    public async Task RegisterAsync_ReturnsNull_WhenEmailAlreadyExists()
    {
        _repoMock.Setup(r => r.EmailExistsAsync("test@test.com")).ReturnsAsync(true);

        var result = await _sut.RegisterAsync(
            new RegisterDto { Email = "test@test.com", Password = "password123" });

        result.Should().BeNull();
    }

    [Fact]
    public async Task RegisterAsync_ReturnsToken_WhenSuccessful()
    {
        _repoMock.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(new User { Id = 1, Email = "test@test.com", PasswordHash = "" });

        var result = await _sut.RegisterAsync(
            new RegisterDto { Email = "test@test.com", Password = "password123" });

        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrEmpty();
        result.UserId.Should().Be(1);
    }

    [Fact]
    public async Task LoginAsync_ReturnsNull_WhenUserNotFound()
    {
        _repoMock.Setup(r => r.GetByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

        var result = await _sut.LoginAsync(
            new LoginDto { Email = "nobody@test.com", Password = "pass" });

        result.Should().BeNull();
    }
}