using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

[TestFixture]
public class AuthServiceTests
{
    private Mock<IUserRepository> _userRepo = null!;
    private Mock<IOtpRepository> _otpRepo = null!;
    private Mock<IOtpSender> _otpSender = null!;
    private Mock<ILogger<AuthService>> _logger = null!;
    private IConfiguration _config = null!;
    private AuthService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _userRepo = new Mock<IUserRepository>();
        _otpRepo = new Mock<IOtpRepository>();
        _otpSender = new Mock<IOtpSender>();
        _logger = new Mock<ILogger<AuthService>>();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "super-secret-key-for-testing-must-be-32-chars!",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
                ["Jwt:ExpiryDays"] = "7",
                ["Otp:ExpiryMinutes"] = "5",
                ["Otp:MaxAttempts"] = "3"
            })
            .Build();

        _sut = new AuthService(_userRepo.Object, _otpRepo.Object, _otpSender.Object, _config, _logger.Object);
    }

    // ─── RegisterAsync ────────────────────────────────────────────────────────

    [Test]
    public async Task RegisterAsync_WhenEmailAlreadyExists_ReturnsNullAndNeverCreatesUser()
    {
        _userRepo.Setup(r => r.EmailExistsAsync("test@example.com")).ReturnsAsync(true);

        var result = await _sut.RegisterAsync(new RegisterDto
        {
            Email = "test@example.com",
            Password = "Password@123"
        });

        result.Should().BeNull();
        _userRepo.Verify(r => r.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Test]
    public async Task RegisterAsync_WhenEmailIsMixedCase_NormalizesBeforeCheck()
    {
        string? checkedEmail = null;
        _userRepo.Setup(r => r.EmailExistsAsync(It.IsAny<string>()))
            .Callback<string>(e => checkedEmail = e)
            .ReturnsAsync(true);

        await _sut.RegisterAsync(new RegisterDto { Email = "TEST@EXAMPLE.COM", Password = "p" });

        checkedEmail.Should().Be("test@example.com");
    }

    [Test]
    public async Task RegisterAsync_WhenMobileAlreadyExists_ReturnsNullAndNeverCreatesUser()
    {
        _userRepo.Setup(r => r.EmailExistsAsync("new@example.com")).ReturnsAsync(false);
        _userRepo.Setup(r => r.MobileNumberExistsAsync("+919876543210")).ReturnsAsync(true);

        var result = await _sut.RegisterAsync(new RegisterDto
        {
            Email = "new@example.com",
            Password = "P@ssw0rd",
            MobileNumber = "9876543210"
        });

        result.Should().BeNull();
        _userRepo.Verify(r => r.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Test]
    public async Task RegisterAsync_WhenValidData_CreatesUserWithHashedPassword()
    {
        _userRepo.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _userRepo.Setup(r => r.MobileNumberExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        User? savedUser = null;
        _userRepo.Setup(r => r.CreateAsync(It.IsAny<User>()))
            .Callback<User>(u => savedUser = u)
            .ReturnsAsync((User u) => { u.Id = 1; return u; });

        await _sut.RegisterAsync(new RegisterDto
        {
            Email = "user@example.com",
            Password = "Password@123"
        });

        savedUser.Should().NotBeNull();
        savedUser!.Email.Should().Be("user@example.com");
        // Password must be hashed, never stored in plain text
        savedUser.PasswordHash.Should().NotBe("Password@123");
        BCrypt.Net.BCrypt.Verify("Password@123", savedUser.PasswordHash).Should().BeTrue();
    }

    [Test]
    public async Task RegisterAsync_WhenValidData_ReturnsJwtToken()
    {
        _userRepo.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _userRepo.Setup(r => r.MobileNumberExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _userRepo.Setup(r => r.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => { u.Id = 5; return u; });

        var result = await _sut.RegisterAsync(new RegisterDto
        {
            Email = "user@example.com",
            Password = "Password@123"
        });

        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrWhiteSpace();
        // Validate token is a proper JWT
        var handler = new JwtSecurityTokenHandler();
        handler.CanReadToken(result.Token).Should().BeTrue();
    }

    [Test]
    public async Task RegisterAsync_WhenValidData_JwtContainsUserIdClaim()
    {
        _userRepo.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _userRepo.Setup(r => r.MobileNumberExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _userRepo.Setup(r => r.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => { u.Id = 7; return u; });

        var result = await _sut.RegisterAsync(new RegisterDto
        {
            Email = "user@example.com",
            Password = "P@ssword"
        });

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result!.Token);

        jwt.Claims.Should().Contain(c => c.Type == "userId" && c.Value == "7");
    }

    [Test]
    public async Task RegisterAsync_WhenMobileProvided_NormalizesWithCountryCode()
    {
        _userRepo.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        string? savedMobile = null;
        _userRepo.Setup(r => r.MobileNumberExistsAsync(It.IsAny<string>()))
            .Callback<string>(m => savedMobile = m)
            .ReturnsAsync(false);
        _userRepo.Setup(r => r.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => { u.Id = 1; return u; });

        await _sut.RegisterAsync(new RegisterDto
        {
            Email = "x@x.com",
            Password = "p",
            MobileNumber = "9876543210"  // no +91
        });

        savedMobile.Should().Be("+919876543210");
    }

    // ─── LoginAsync ───────────────────────────────────────────────────────────

    [Test]
    public async Task LoginAsync_WhenUserNotFound_ReturnsNull()
    {
        _userRepo.Setup(r => r.GetByEmailAsync("missing@example.com")).ReturnsAsync((User?)null);

        var result = await _sut.LoginAsync(new LoginDto
        {
            Email = "missing@example.com",
            Password = "P@ssword"
        });

        result.Should().BeNull();
    }

    [Test]
    public async Task LoginAsync_WhenPasswordIsWrong_ReturnsNull()
    {
        var user = new User
        {
            Id = 1,
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("CorrectPassword")
        };

        _userRepo.Setup(r => r.GetByEmailAsync("user@example.com")).ReturnsAsync(user);

        var result = await _sut.LoginAsync(new LoginDto
        {
            Email = "user@example.com",
            Password = "WrongPassword"
        });

        result.Should().BeNull();
    }

    [Test]
    public async Task LoginAsync_WhenPasswordIsCorrect_ReturnsJwtToken()
    {
        var user = new User
        {
            Id = 3,
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123")
        };

        _userRepo.Setup(r => r.GetByEmailAsync("user@example.com")).ReturnsAsync(user);

        var result = await _sut.LoginAsync(new LoginDto
        {
            Email = "user@example.com",
            Password = "Password@123"
        });

        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrWhiteSpace();
        var handler = new JwtSecurityTokenHandler();
        handler.CanReadToken(result.Token).Should().BeTrue();
    }

    [Test]
    public async Task LoginAsync_WhenLoginSucceeds_UpdatesLastLoginTimestamp()
    {
        var user = new User
        {
            Id = 3,
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123")
        };

        _userRepo.Setup(r => r.GetByEmailAsync("user@example.com")).ReturnsAsync(user);

        await _sut.LoginAsync(new LoginDto
        {
            Email = "user@example.com",
            Password = "Password@123"
        });

        user.LastLoginAtUtc.Should().NotBeNull();
        user.LastLoginAtUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
        _userRepo.Verify(r => r.UpdateAsync(user), Times.Once);
    }

    [Test]
    public async Task LoginAsync_WhenEmailHasMixedCase_FindsUserAfterNormalization()
    {
        var user = new User
        {
            Id = 1,
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass@123")
        };

        _userRepo.Setup(r => r.GetByEmailAsync("user@example.com")).ReturnsAsync(user);

        var result = await _sut.LoginAsync(new LoginDto
        {
            Email = "USER@EXAMPLE.COM",
            Password = "Pass@123"
        });

        result.Should().NotBeNull();
    }

    [Test]
    public async Task LoginAsync_WhenUserHasNoPasswordHash_ReturnsNull()
    {
        // Google-only accounts have empty PasswordHash
        var user = new User
        {
            Id = 2,
            Email = "google@example.com",
            PasswordHash = string.Empty
        };

        _userRepo.Setup(r => r.GetByEmailAsync("google@example.com")).ReturnsAsync(user);

        var result = await _sut.LoginAsync(new LoginDto
        {
            Email = "google@example.com",
            Password = "any"
        });

        result.Should().BeNull();
    }

    [Test]
    public async Task LoginAsync_JwtTokenContainsCorrectUserIdClaim()
    {
        var user = new User
        {
            Id = 42,
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass@123")
        };

        _userRepo.Setup(r => r.GetByEmailAsync("user@example.com")).ReturnsAsync(user);

        var result = await _sut.LoginAsync(new LoginDto
        {
            Email = "user@example.com",
            Password = "Pass@123"
        });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result!.Token);
        jwt.Claims.Should().Contain(c => c.Type == "userId" && c.Value == "42");
        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.NameIdentifier && c.Value == "42");
    }

    // ─── SendMobileOtpAsync ───────────────────────────────────────────────────

    [Test]
    public async Task SendMobileOtpAsync_AlwaysReturnsTrue()
    {
        var result = await _sut.SendMobileOtpAsync(new SendOtpDto { MobileNumber = "9876543210" });

        result.Should().BeTrue();
    }

    [Test]
    public async Task SendMobileOtpAsync_StoresHashedOtpInRepository()
    {
        OtpRequest? saved = null;
        _otpRepo.Setup(r => r.CreateAsync(It.IsAny<OtpRequest>()))
            .Callback<OtpRequest>(o => saved = o);

        await _sut.SendMobileOtpAsync(new SendOtpDto { MobileNumber = "9876543210" });

        saved.Should().NotBeNull();
        saved!.OtpHash.Should().NotBeNullOrWhiteSpace();
        // OTP hash must NOT equal the raw OTP (must be bcrypt-hashed)
        saved.OtpHash.Should().NotMatchRegex(@"^\d{6}$");
        saved.MobileNumber.Should().Be("+919876543210");
    }

    [Test]
    public async Task SendMobileOtpAsync_OtpExpiresInConfiguredMinutes()
    {
        OtpRequest? saved = null;
        _otpRepo.Setup(r => r.CreateAsync(It.IsAny<OtpRequest>()))
            .Callback<OtpRequest>(o => saved = o);

        await _sut.SendMobileOtpAsync(new SendOtpDto { MobileNumber = "9876543210" });

        saved!.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), precision: TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task SendMobileOtpAsync_SendsSixDigitOtpViaSender()
    {
        string? sentOtp = null;
        _otpSender.Setup(s => s.SendOtpAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, otp) => sentOtp = otp);

        await _sut.SendMobileOtpAsync(new SendOtpDto { MobileNumber = "+919876543210" });

        sentOtp.Should().NotBeNull();
        sentOtp!.Length.Should().Be(6);
        sentOtp.Should().MatchRegex(@"^\d{6}$");
    }

    // ─── VerifyMobileOtpAsync ─────────────────────────────────────────────────

    [Test]
    public async Task VerifyMobileOtpAsync_WhenNoActiveOtp_ReturnsNull()
    {
        _otpRepo.Setup(r => r.GetLatestActiveAsync(It.IsAny<string>()))
            .ReturnsAsync((OtpRequest?)null);

        var result = await _sut.VerifyMobileOtpAsync(new VerifyOtpDto
        {
            MobileNumber = "9876543210",
            Otp = "123456"
        });

        result.Should().BeNull();
    }

    [Test]
    public async Task VerifyMobileOtpAsync_WhenOtpIsWrong_IncrementsFailedAttemptsAndReturnsNull()
    {
        var otpRequest = BuildOtpRequest("123456", failedAttempts: 0);
        _otpRepo.Setup(r => r.GetLatestActiveAsync("+919876543210")).ReturnsAsync(otpRequest);

        var result = await _sut.VerifyMobileOtpAsync(new VerifyOtpDto
        {
            MobileNumber = "9876543210",
            Otp = "000000"  // wrong OTP
        });

        result.Should().BeNull();
        otpRequest.FailedAttempts.Should().Be(1);
        _otpRepo.Verify(r => r.UpdateAsync(otpRequest), Times.Once);
    }

    [Test]
    public async Task VerifyMobileOtpAsync_WhenMaxAttemptsReached_ReturnsNull()
    {
        // MaxAttempts = 3 in test config; if failedAttempts is already 3, block
        var otpRequest = BuildOtpRequest("123456", failedAttempts: 3);
        _otpRepo.Setup(r => r.GetLatestActiveAsync("+919876543210")).ReturnsAsync(otpRequest);

        var result = await _sut.VerifyMobileOtpAsync(new VerifyOtpDto
        {
            MobileNumber = "9876543210",
            Otp = "123456"  // correct OTP but blocked
        });

        result.Should().BeNull();
    }

    [Test]
    public async Task VerifyMobileOtpAsync_WhenOtpIsCorrectAndUserExists_ReturnsTokenAndMarksUsed()
    {
        var otpRequest = BuildOtpRequest("123456", failedAttempts: 0);
        var user = new User { Id = 10, Email = "user@example.com", MobileNumber = "+919876543210", IsMobileVerified = true };

        _otpRepo.Setup(r => r.GetLatestActiveAsync("+919876543210")).ReturnsAsync(otpRequest);
        _userRepo.Setup(r => r.GetByMobileNumberAsync("+919876543210")).ReturnsAsync(user);

        var result = await _sut.VerifyMobileOtpAsync(new VerifyOtpDto
        {
            MobileNumber = "9876543210",
            Otp = "123456"
        });

        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrWhiteSpace();
        otpRequest.UsedAtUtc.Should().NotBeNull();
        _otpRepo.Verify(r => r.UpdateAsync(otpRequest), Times.Once);
    }

    [Test]
    public async Task VerifyMobileOtpAsync_WhenNewUserFirstLogin_CreatesUserWithSyntheticEmail()
    {
        var otpRequest = BuildOtpRequest("654321", failedAttempts: 0);
        _otpRepo.Setup(r => r.GetLatestActiveAsync("+919876543210")).ReturnsAsync(otpRequest);
        _userRepo.Setup(r => r.GetByMobileNumberAsync("+919876543210")).ReturnsAsync((User?)null);

        User? createdUser = null;
        _userRepo.Setup(r => r.CreateAsync(It.IsAny<User>()))
            .Callback<User>(u => createdUser = u)
            .ReturnsAsync((User u) => { u.Id = 99; return u; });

        var result = await _sut.VerifyMobileOtpAsync(new VerifyOtpDto
        {
            MobileNumber = "9876543210",
            Otp = "654321"
        });

        result.Should().NotBeNull();
        createdUser.Should().NotBeNull();
        createdUser!.MobileNumber.Should().Be("+919876543210");
        createdUser.IsMobileVerified.Should().BeTrue();
        createdUser.Email.Should().Contain("mobile-");
    }

    [Test]
    public async Task VerifyMobileOtpAsync_WhenUserExistsButNotMobileVerified_MarksVerified()
    {
        var otpRequest = BuildOtpRequest("123456", failedAttempts: 0);
        var user = new User
        {
            Id = 5,
            Email = "user@example.com",
            MobileNumber = "+919876543210",
            IsMobileVerified = false   // not yet verified
        };

        _otpRepo.Setup(r => r.GetLatestActiveAsync("+919876543210")).ReturnsAsync(otpRequest);
        _userRepo.Setup(r => r.GetByMobileNumberAsync("+919876543210")).ReturnsAsync(user);

        await _sut.VerifyMobileOtpAsync(new VerifyOtpDto
        {
            MobileNumber = "9876543210",
            Otp = "123456"
        });

        user.IsMobileVerified.Should().BeTrue();
        _userRepo.Verify(r => r.UpdateAsync(user), Times.AtLeast(1));
    }

    // ─── ChangePasswordAsync ──────────────────────────────────────────────────

    [Test]
    public async Task ChangePasswordAsync_WhenCurrentPasswordIsWrong_ReturnsFalse()
    {
        var user = new User
        {
            Id = 1,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("CurrentPass@123")
        };
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        var result = await _sut.ChangePasswordAsync(1, new ChangePasswordDto
        {
            CurrentPassword = "WrongPass",
            NewPassword = "NewPass@123"
        });

        result.Should().BeFalse();
        _userRepo.Verify(r => r.UpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Test]
    public async Task ChangePasswordAsync_WhenCurrentPasswordIsCorrect_UpdatesHashAndReturnsTrue()
    {
        var user = new User
        {
            Id = 1,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("CurrentPass@123")
        };
        _userRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        var result = await _sut.ChangePasswordAsync(1, new ChangePasswordDto
        {
            CurrentPassword = "CurrentPass@123",
            NewPassword = "NewPass@123"
        });

        result.Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("NewPass@123", user.PasswordHash).Should().BeTrue();
        _userRepo.Verify(r => r.UpdateAsync(user), Times.Once);
    }

    [Test]
    public async Task ChangePasswordAsync_WhenUserIsGoogleOnlyAccount_ReturnsFalse()
    {
        var user = new User
        {
            Id = 2,
            PasswordHash = string.Empty  // Google-only, no password
        };
        _userRepo.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(user);

        var result = await _sut.ChangePasswordAsync(2, new ChangePasswordDto
        {
            CurrentPassword = "anything",
            NewPassword = "NewPass@123"
        });

        result.Should().BeFalse();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static OtpRequest BuildOtpRequest(string plainOtp, int failedAttempts) => new()
    {
        Id = 1,
        MobileNumber = "+919876543210",
        OtpHash = BCrypt.Net.BCrypt.HashPassword(plainOtp),
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
        FailedAttempts = failedAttempts
    };
}