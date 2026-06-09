using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

[TestFixture]
public class AuthControllerTests
{
    private Mock<IAuthService> _authServiceMock = null!;
    private Mock<ILogger<AuthController>> _loggerMock = null!;
    private AuthController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _authServiceMock = new Mock<IAuthService>();
        _loggerMock = new Mock<ILogger<AuthController>>();
        _controller = new AuthController(_authServiceMock.Object, _loggerMock.Object);
    }

    [Test]
    public async Task Register_WhenUserAlreadyExists_ReturnsConflict()
    {
        _authServiceMock
            .Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
            .ReturnsAsync((AuthResponseDto?)null);

        IActionResult result = await _controller.Register(new RegisterDto
        {
            Email = "taken@example.com",
            Password = "Password@123"
        });

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Test]
    public async Task Register_WhenSuccess_ReturnsOk_WithOnlyToken()
    {
        var response = new AuthResponseDto
        {
            Token = "jwt-token"
        };

        _authServiceMock
            .Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
            .ReturnsAsync(response);

        IActionResult result = await _controller.Register(new RegisterDto
        {
            Email = "user@example.com",
            Password = "Password@123"
        });

        OkObjectResult okResult = result.Should()
            .BeOfType<OkObjectResult>()
            .Subject;

        var apiResponse = okResult.Value.Should()
            .BeOfType<ApiResponse<AuthResponseDto>>()
            .Subject;

        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().NotBeNull();
        apiResponse.Data!.Token.Should().Be("jwt-token");

        typeof(AuthResponseDto).GetProperty("Email").Should().BeNull();
        typeof(AuthResponseDto).GetProperty("UserId").Should().BeNull();
        typeof(AuthResponseDto).GetProperty("MobileNumber").Should().BeNull();
    }

    [Test]
    public async Task Login_WhenInvalidCredentials_ReturnsUnauthorized()
    {
        _authServiceMock
            .Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync((AuthResponseDto?)null);

        IActionResult result = await _controller.Login(new LoginDto
        {
            Email = "wrong@example.com",
            Password = "wrong"
        });

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Test]
    public async Task Login_WhenValidCredentials_ReturnsOk_WithOnlyToken()
    {
        var response = new AuthResponseDto
        {
            Token = "jwt-token"
        };

        _authServiceMock
            .Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(response);

        IActionResult result = await _controller.Login(new LoginDto
        {
            Email = "user@example.com",
            Password = "Password@123"
        });

        OkObjectResult okResult = result.Should()
            .BeOfType<OkObjectResult>()
            .Subject;

        var apiResponse = okResult.Value.Should()
            .BeOfType<ApiResponse<AuthResponseDto>>()
            .Subject;

        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().NotBeNull();
        apiResponse.Data!.Token.Should().Be("jwt-token");

        typeof(AuthResponseDto).GetProperty("Email").Should().BeNull();
        typeof(AuthResponseDto).GetProperty("UserId").Should().BeNull();
        typeof(AuthResponseDto).GetProperty("MobileNumber").Should().BeNull();
    }

    [Test]
    public async Task SendOtp_WhenCalled_ReturnsOk()
    {
        _authServiceMock
            .Setup(s => s.SendMobileOtpAsync(It.IsAny<SendOtpDto>()))
            .ReturnsAsync(true);

        IActionResult result = await _controller.SendOtp(new SendOtpDto
        {
            MobileNumber = "+919876543210"
        });

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task VerifyOtp_WhenInvalidOtp_ReturnsUnauthorized()
    {
        _authServiceMock
            .Setup(s => s.VerifyMobileOtpAsync(It.IsAny<VerifyOtpDto>()))
            .ReturnsAsync((AuthResponseDto?)null);

        IActionResult result = await _controller.VerifyOtp(new VerifyOtpDto
        {
            MobileNumber = "+919876543210",
            Otp = "000000"
        });

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Test]
    public async Task VerifyOtp_WhenValidOtp_ReturnsOk_WithOnlyToken()
    {
        var response = new AuthResponseDto
        {
            Token = "otp-token"
        };

        _authServiceMock
            .Setup(s => s.VerifyMobileOtpAsync(It.IsAny<VerifyOtpDto>()))
            .ReturnsAsync(response);

        IActionResult result = await _controller.VerifyOtp(new VerifyOtpDto
        {
            MobileNumber = "+919876543210",
            Otp = "123456"
        });

        OkObjectResult okResult = result.Should()
            .BeOfType<OkObjectResult>()
            .Subject;

        var apiResponse = okResult.Value.Should()
            .BeOfType<ApiResponse<AuthResponseDto>>()
            .Subject;

        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().NotBeNull();
        apiResponse.Data!.Token.Should().Be("otp-token");

        typeof(AuthResponseDto).GetProperty("Email").Should().BeNull();
        typeof(AuthResponseDto).GetProperty("UserId").Should().BeNull();
        typeof(AuthResponseDto).GetProperty("MobileNumber").Should().BeNull();
    }
}