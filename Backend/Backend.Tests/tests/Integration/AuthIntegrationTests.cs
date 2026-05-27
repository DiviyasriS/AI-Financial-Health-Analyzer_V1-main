using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Moq;
using NUnit.Framework;

[TestFixture]
public class AuthIntegrationTests
{
    private CustomWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Register_WhenUserIsNew_ReturnsOk()
    {
        _factory.AuthServiceMock
            .Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
            .ReturnsAsync(new AuthResponseDto
            {
                UserId = 1,
                Email = "test@example.com",
                Token = "fake-jwt-token"
            });

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = "test@example.com",
            Password = "Password@123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Register_WhenUserAlreadyExists_ReturnsConflict()
    {
        _factory.AuthServiceMock
            .Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
            .ReturnsAsync((AuthResponseDto?)null);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = "test@example.com",
            Password = "Password@123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Login_WhenCredentialsAreValid_ReturnsOk()
    {
        _factory.AuthServiceMock
            .Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(new AuthResponseDto
            {
                UserId = 1,
                Email = "test@example.com",
                Token = "fake-jwt-token"
            });

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/auth/login", new LoginDto
        {
            Email = "test@example.com",
            Password = "Password@123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Login_WhenCredentialsAreInvalid_ReturnsUnauthorized()
    {
        _factory.AuthServiceMock
            .Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync((AuthResponseDto?)null);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/auth/login", new LoginDto
        {
            Email = "wrong@example.com",
            Password = "wrong"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task SendOtp_WhenCalled_ReturnsOk()
    {
        _factory.AuthServiceMock
            .Setup(s => s.SendMobileOtpAsync(It.IsAny<SendOtpDto>()))
            .ReturnsAsync(true);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/auth/otp/send", new SendOtpDto
        {
            MobileNumber = "+919876543210"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task VerifyOtp_WhenOtpIsValid_ReturnsOk()
    {
        _factory.AuthServiceMock
            .Setup(s => s.VerifyMobileOtpAsync(It.IsAny<VerifyOtpDto>()))
            .ReturnsAsync(new AuthResponseDto
            {
                UserId = 1,
                Email = "mobile-user@example.com",
                MobileNumber = "+919876543210",
                Token = "fake-jwt-token"
            });

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/auth/otp/verify", new VerifyOtpDto
        {
            MobileNumber = "+919876543210",
            Otp = "123456"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task VerifyOtp_WhenOtpIsInvalid_ReturnsUnauthorized()
    {
        _factory.AuthServiceMock
            .Setup(s => s.VerifyMobileOtpAsync(It.IsAny<VerifyOtpDto>()))
            .ReturnsAsync((AuthResponseDto?)null);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/auth/otp/verify", new VerifyOtpDto
        {
            MobileNumber = "+919876543210",
            Otp = "000000"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}