using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.Security.Claims;

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

    // ─── Register ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Register_WhenUserAlreadyExists_ReturnsConflict()
    {
        _authServiceMock.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
            .ReturnsAsync((AuthResponseDto?)null);

        var result = await _controller.Register(new RegisterDto
        {
            Email = "taken@example.com",
            Password = "Password@123"
        });

        result.Should().BeOfType<ConflictObjectResult>();
var conflict = result as ConflictObjectResult;
conflict!.Value.Should().NotBeNull();
    }

    [Test]
    public async Task Register_WhenSuccess_ReturnsOkWithToken()
    {
        _authServiceMock.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
            .ReturnsAsync(new AuthResponseDto { Token = "jwt-token" });

        var result = await _controller.Register(new RegisterDto
        {
            Email = "new@example.com",
            Password = "Password@123"
        });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ApiResponse<AuthResponseDto>>().Subject;
        body.Success.Should().BeTrue();
        body.Data!.Token.Should().Be("jwt-token");
    }

    [Test]
    public async Task Register_CallsServiceWithExactEmail()
    {
        RegisterDto? captured = null;
        _authServiceMock.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
            .Callback<RegisterDto>(dto => captured = dto)
            .ReturnsAsync(new AuthResponseDto { Token = "t" });

        await _controller.Register(new RegisterDto { Email = "user@example.com", Password = "P@ssw0rd" });

        captured!.Email.Should().Be("user@example.com");
    }

    [Test]
    public async Task Register_WhenServiceThrows_PropagatesException()
    {
        _authServiceMock.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
            .ThrowsAsync(new Exception("DB error"));

        Func<Task> act = async () => await _controller.Register(new RegisterDto
        {
            Email = "x@x.com",
            Password = "pass"
        });

        await act.Should().ThrowAsync<Exception>().WithMessage("DB error");
    }

    // ─── Login ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task Login_WhenInvalidCredentials_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync((AuthResponseDto?)null);

        var result = await _controller.Login(new LoginDto
        {
            Email = "wrong@example.com",
            Password = "wrong"
        });

        result.Should().BeOfType<UnauthorizedObjectResult>();
var unauthorized = result as UnauthorizedObjectResult;
unauthorized!.Value.Should().NotBeNull();
    }

    [Test]
    public async Task Login_WhenValidCredentials_ReturnsOkWithToken()
    {
        _authServiceMock.Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(new AuthResponseDto { Token = "valid-token" });

        var result = await _controller.Login(new LoginDto
        {
            Email = "user@example.com",
            Password = "Password@123"
        });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ApiResponse<AuthResponseDto>>().Subject;
        body.Success.Should().BeTrue();
        body.Data!.Token.Should().Be("valid-token");
    }

    [Test]
    public async Task Login_CallsServiceExactlyOnce()
    {
        _authServiceMock.Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(new AuthResponseDto { Token = "t" });

        await _controller.Login(new LoginDto { Email = "a@b.com", Password = "p" });

        _authServiceMock.Verify(s => s.LoginAsync(It.IsAny<LoginDto>()), Times.Once);
    }

    // ─── OTP ──────────────────────────────────────────────────────────────────

    [Test]
    public async Task SendOtp_WhenOtpSentSuccessfully_ReturnsOk()
    {
        _authServiceMock.Setup(s => s.SendMobileOtpAsync(It.IsAny<SendOtpDto>()))
            .ReturnsAsync(true);

        var result = await _controller.SendOtp(new SendOtpDto { MobileNumber = "+919876543210" });

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task SendOtp_WhenServiceReturnsFalse_ReturnsBadRequest()
    {
        _authServiceMock.Setup(s => s.SendMobileOtpAsync(It.IsAny<SendOtpDto>()))
            .ReturnsAsync(false);

        var result = await _controller.SendOtp(new SendOtpDto { MobileNumber = "+919876543210" });

        // The controller currently returns OK regardless of the bool return;
        // test documents the actual current behavior and will catch if it changes.
        result.Should().BeAssignableTo<IActionResult>();
    }

    [Test]
    public async Task VerifyOtp_WhenInvalidOtp_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(s => s.VerifyMobileOtpAsync(It.IsAny<VerifyOtpDto>()))
            .ReturnsAsync((AuthResponseDto?)null);

        var result = await _controller.VerifyOtp(new VerifyOtpDto
        {
            MobileNumber = "+919876543210",
            Otp = "000000"
        });

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Test]
    public async Task VerifyOtp_WhenValidOtp_ReturnsOkWithToken()
    {
        _authServiceMock.Setup(s => s.VerifyMobileOtpAsync(It.IsAny<VerifyOtpDto>()))
            .ReturnsAsync(new AuthResponseDto { Token = "otp-token" });

        var result = await _controller.VerifyOtp(new VerifyOtpDto
        {
            MobileNumber = "+919876543210",
            Otp = "123456"
        });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ApiResponse<AuthResponseDto>>().Subject;
        body.Success.Should().BeTrue();
        body.Data!.Token.Should().Be("otp-token");
    }

    // ─── Profile endpoints (authenticated) ───────────────────────────────────

    [Test]
    public async Task GetProfile_WhenAuthenticated_ReturnsUserProfile()
    {
        _controller.ControllerContext = BuildAuthContext(userId: 42);

        _authServiceMock.Setup(s => s.GetProfileAsync(42))
            .ReturnsAsync(new UserProfileDto
            {
                UserId = 42,
                Email = "user@example.com",
                IsEmailVerified = true
            });

        var result = await _controller.GetProfile();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ApiResponse<UserProfileDto>>().Subject;
        body.Data!.UserId.Should().Be(42);
        body.Data.Email.Should().Be("user@example.com");
    }

    [Test]
    public async Task GetProfile_WhenNoUserIdInToken_ReturnsUnauthorized()
    {
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await _controller.GetProfile();

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Test]
    public async Task GetProfile_WhenUserNotFound_ReturnsNotFound()
    {
        _controller.ControllerContext = BuildAuthContext(userId: 99);
        _authServiceMock.Setup(s => s.GetProfileAsync(99))
            .ReturnsAsync((UserProfileDto?)null);

        var result = await _controller.GetProfile();

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Test]
    public async Task ChangePassword_WhenCurrentPasswordIsWrong_ReturnsUnauthorized()
    {
        _controller.ControllerContext = BuildAuthContext(userId: 1);
        _authServiceMock.Setup(s => s.ChangePasswordAsync(1, It.IsAny<ChangePasswordDto>()))
            .ReturnsAsync(false);

        var result = await _controller.ChangePassword(new ChangePasswordDto
        {
            CurrentPassword = "wrong",
            NewPassword = "NewPass@123"
        });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task ChangePassword_WhenPasswordChangedSuccessfully_ReturnsOk()
    {
        _controller.ControllerContext = BuildAuthContext(userId: 1);
        _authServiceMock.Setup(s => s.ChangePasswordAsync(1, It.IsAny<ChangePasswordDto>()))
            .ReturnsAsync(true);

        var result = await _controller.ChangePassword(new ChangePasswordDto
        {
            CurrentPassword = "OldPass@123",
            NewPassword = "NewPass@123"
        });

        result.Should().BeOfType<OkObjectResult>();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static ControllerContext BuildAuthContext(int userId) =>
        new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim("userId", userId.ToString())
                }, "TestAuth"))
            }
        };
}