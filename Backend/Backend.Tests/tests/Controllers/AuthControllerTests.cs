using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly Mock<ILogger<AuthController>> _loggerMock;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _authServiceMock = new Mock<IAuthService>();
        _loggerMock = new Mock<ILogger<AuthController>>();
        _controller = new AuthController(_authServiceMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Register_WhenEmailTaken_Returns409()
    {
        // Arrange
        _authServiceMock.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
            .ReturnsAsync((AuthResponseDto?)null);

        // Act
        var result = await _controller.Register(new RegisterDto
        {
            Email = "taken@email.com",
            Password = "password123"
        });

        // Assert
        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Register_WhenSuccess_Returns200WithToken()
    {
        // Arrange
        var authResponse = new AuthResponseDto
        {
            Token = "test.jwt.token",
            Email = "new@email.com",
            UserId = 1
        };
        _authServiceMock.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
            .ReturnsAsync(authResponse);

        // Act
        var result = await _controller.Register(new RegisterDto
        {
            Email = "new@email.com",
            Password = "password123"
        });

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<AuthResponseDto>>().Subject;
        response.Success.Should().BeTrue();
        response.Data!.Token.Should().Be("test.jwt.token");
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_Returns401()
    {
        // Arrange
        _authServiceMock.Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync((AuthResponseDto?)null);

        // Act
        var result = await _controller.Login(new LoginDto
        {
            Email = "user@email.com",
            Password = "wrongpass"
        });

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}