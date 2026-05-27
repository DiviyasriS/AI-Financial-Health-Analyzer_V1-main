using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

[TestFixture]
public class AuthServiceTests
{
    private Mock<IUserRepository> _userRepositoryMock = null!;
    private Mock<IOtpRepository> _otpRepositoryMock = null!;
    private Mock<IOtpSender> _otpSenderMock = null!;
    private Mock<ILogger<AuthService>> _loggerMock = null!;
    private IConfiguration _configuration = null!;
    private AuthService _authService = null!;

    [SetUp]
    public void SetUp()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _otpRepositoryMock = new Mock<IOtpRepository>();
        _otpSenderMock = new Mock<IOtpSender>();
        _loggerMock = new Mock<ILogger<AuthService>>();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "super-secret-key-for-testing-1234567890",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
                ["Jwt:ExpiryDays"] = "7",
                ["Otp:ExpiryMinutes"] = "5",
                ["Otp:MaxAttempts"] = "5"
            })
            .Build();

        _authService = new AuthService(
            _userRepositoryMock.Object,
            _otpRepositoryMock.Object,
            _otpSenderMock.Object,
            _configuration,
            _loggerMock.Object
        );
    }

    [Test]
    public async Task RegisterAsync_WhenEmailAlreadyExists_ReturnsNull()
    {
        _userRepositoryMock
            .Setup(r => r.EmailExistsAsync("test@example.com"))
            .ReturnsAsync(true);

        AuthResponseDto? result = await _authService.RegisterAsync(new RegisterDto
        {
            Email = "test@example.com",
            Password = "Password@123"
        });

        result.Should().BeNull();

        _userRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Test]
    public async Task RegisterAsync_WhenMobileAlreadyExists_ReturnsNull()
    {
        _userRepositoryMock
            .Setup(r => r.EmailExistsAsync("test@example.com"))
            .ReturnsAsync(false);

        _userRepositoryMock
            .Setup(r => r.MobileNumberExistsAsync("+919876543210"))
            .ReturnsAsync(true);

        AuthResponseDto? result = await _authService.RegisterAsync(new RegisterDto
        {
            Email = "test@example.com",
            Password = "Password@123",
            MobileNumber = "9876543210"
        });

        result.Should().BeNull();

        _userRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Test]
    public async Task RegisterAsync_WhenValidUser_CreatesUserAndReturnsToken()
    {
        _userRepositoryMock
            .Setup(r => r.EmailExistsAsync("test@example.com"))
            .ReturnsAsync(false);

        _userRepositoryMock
            .Setup(r => r.MobileNumberExistsAsync("+919876543210"))
            .ReturnsAsync(false);

        _userRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync((User user) =>
            {
                user.Id = 1;
                return user;
            });

        AuthResponseDto? result = await _authService.RegisterAsync(new RegisterDto
        {
            Email = "TEST@EXAMPLE.COM",
            Password = "Password@123",
            MobileNumber = "9876543210"
        });

        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrWhiteSpace();
        result.Email.Should().Be("test@example.com");
        result.MobileNumber.Should().Be("+919876543210");
        result.UserId.Should().Be(1);
    }

    [Test]
    public async Task LoginAsync_WhenUserNotFound_ReturnsNull()
    {
        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("missing@example.com"))
            .ReturnsAsync((User?)null);

        AuthResponseDto? result = await _authService.LoginAsync(new LoginDto
        {
            Email = "missing@example.com",
            Password = "Password@123"
        });

        result.Should().BeNull();
    }

    [Test]
    public async Task LoginAsync_WhenPasswordIsWrong_ReturnsNull()
    {
        var user = new User
        {
            Id = 1,
            Email = "test@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("CorrectPassword@123")
        };

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("test@example.com"))
            .ReturnsAsync(user);

        AuthResponseDto? result = await _authService.LoginAsync(new LoginDto
        {
            Email = "test@example.com",
            Password = "WrongPassword"
        });

        result.Should().BeNull();
    }

    [Test]
    public async Task LoginAsync_WhenPasswordIsCorrect_ReturnsToken()
    {
        var user = new User
        {
            Id = 1,
            Email = "test@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123")
        };

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("test@example.com"))
            .ReturnsAsync(user);

        AuthResponseDto? result = await _authService.LoginAsync(new LoginDto
        {
            Email = "test@example.com",
            Password = "Password@123"
        });

        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrWhiteSpace();
        result.Email.Should().Be("test@example.com");

        _userRepositoryMock.Verify(r => r.UpdateAsync(user), Times.Once);
    }

    [Test]
    public async Task SendMobileOtpAsync_WhenCalled_CreatesOtpAndSendsIt()
    {
        bool result = await _authService.SendMobileOtpAsync(new SendOtpDto
        {
            MobileNumber = "9876543210"
        });

        result.Should().BeTrue();

        _otpRepositoryMock.Verify(r => r.CreateAsync(It.Is<OtpRequest>(
            o => o.MobileNumber == "+919876543210"
                 && !string.IsNullOrWhiteSpace(o.OtpHash)
                 && o.ExpiresAtUtc > DateTime.UtcNow
        )), Times.Once);

        _otpSenderMock.Verify(s => s.SendOtpAsync(
            "+919876543210",
            It.Is<string>(otp => otp.Length == 6)
        ), Times.Once);
    }

    [Test]
    public async Task VerifyMobileOtpAsync_WhenNoActiveOtp_ReturnsNull()
    {
        _otpRepositoryMock
            .Setup(r => r.GetLatestActiveAsync("+919876543210"))
            .ReturnsAsync((OtpRequest?)null);

        AuthResponseDto? result = await _authService.VerifyMobileOtpAsync(new VerifyOtpDto
        {
            MobileNumber = "9876543210",
            Otp = "123456"
        });

        result.Should().BeNull();
    }

    [Test]
    public async Task VerifyMobileOtpAsync_WhenOtpIsWrong_ReturnsNullAndIncrementsAttempts()
    {
        var otpRequest = new OtpRequest
        {
            Id = 1,
            MobileNumber = "+919876543210",
            OtpHash = BCrypt.Net.BCrypt.HashPassword("123456"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            FailedAttempts = 0
        };

        _otpRepositoryMock
            .Setup(r => r.GetLatestActiveAsync("+919876543210"))
            .ReturnsAsync(otpRequest);

        AuthResponseDto? result = await _authService.VerifyMobileOtpAsync(new VerifyOtpDto
        {
            MobileNumber = "9876543210",
            Otp = "000000"
        });

        result.Should().BeNull();
        otpRequest.FailedAttempts.Should().Be(1);

        _otpRepositoryMock.Verify(r => r.UpdateAsync(otpRequest), Times.Once);
    }

    [Test]
    public async Task VerifyMobileOtpAsync_WhenOtpIsCorrectAndUserExists_ReturnsToken()
    {
        var otpRequest = new OtpRequest
        {
            Id = 1,
            MobileNumber = "+919876543210",
            OtpHash = BCrypt.Net.BCrypt.HashPassword("123456"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            FailedAttempts = 0
        };

        var user = new User
        {
            Id = 10,
            Email = "test@example.com",
            MobileNumber = "+919876543210",
            IsMobileVerified = true
        };

        _otpRepositoryMock
            .Setup(r => r.GetLatestActiveAsync("+919876543210"))
            .ReturnsAsync(otpRequest);

        _userRepositoryMock
            .Setup(r => r.GetByMobileNumberAsync("+919876543210"))
            .ReturnsAsync(user);

        AuthResponseDto? result = await _authService.VerifyMobileOtpAsync(new VerifyOtpDto
        {
            MobileNumber = "9876543210",
            Otp = "123456"
        });

        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrWhiteSpace();
        result.UserId.Should().Be(10);
        result.MobileNumber.Should().Be("+919876543210");

        otpRequest.UsedAtUtc.Should().NotBeNull();
        _otpRepositoryMock.Verify(r => r.UpdateAsync(otpRequest), Times.Once);
        _userRepositoryMock.Verify(r => r.UpdateAsync(user), Times.Once);
    }
}