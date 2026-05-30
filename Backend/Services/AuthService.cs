using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public class AuthService : IAuthService
{
    private const string GoogleProvider = "Google";
    private readonly IUserRepository _userRepository;
    private readonly IOtpRepository _otpRepository;
    private readonly IOtpSender _otpSender;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        IOtpRepository otpRepository,
        IOtpSender otpSender,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _otpRepository = otpRepository;
        _otpSender = otpSender;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AuthResponseDto?> RegisterAsync(RegisterDto dto)
    {
        string normalizedEmail = NormalizeEmail(dto.Email);
        string? normalizedMobile = NormalizeMobile(dto.MobileNumber);

        if (await _userRepository.EmailExistsAsync(normalizedEmail))
        {
            _logger.LogDebug("Registration blocked because email already exists: {Email}", normalizedEmail);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(normalizedMobile) && await _userRepository.MobileNumberExistsAsync(normalizedMobile))
        {
            _logger.LogDebug("Registration blocked because mobile already exists: {MobileNumber}", normalizedMobile);
            return null;
        }

        User user = new()
        {
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            MobileNumber = normalizedMobile,
            CreatedAtUtc = DateTime.UtcNow
        };

        try
        {
            User createdUser = await _userRepository.CreateAsync(user);
            _logger.LogInformation("New user created with ID {UserId}", createdUser.Id);
            return BuildAuthResponse(createdUser);
        }
        catch (DbUpdateException ex) when (IsDuplicateConstraint(ex))
        {
            _logger.LogWarning(ex, "Registration duplicate conflict for email/mobile.");
            return null;
        }
    }

    public async Task<AuthResponseDto?> LoginAsync(LoginDto dto)
    {
        string normalizedEmail = NormalizeEmail(dto.Email);
        User? user = await _userRepository.GetByEmailAsync(normalizedEmail);

        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            BCrypt.Net.BCrypt.Verify("dummy", BCrypt.Net.BCrypt.HashPassword("dummy"));
            _logger.LogWarning("Login failed for email: {Email}", normalizedEmail);
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed due to invalid password for email: {Email}", normalizedEmail);
            return null;
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);
        _logger.LogInformation("User {UserId} logged in with email/password.", user.Id);
        return BuildAuthResponse(user);
    }

    public async Task<AuthResponseDto?> GoogleLoginAsync(GoogleLoginDto dto)
    {
        string? clientId = _configuration["Authentication:Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Google ClientId is not configured.");
        }

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(dto.Credential, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid Google Sign-In credential received.");
            return null;
        }

        if (!payload.EmailVerified)
        {
            _logger.LogWarning("Google login rejected because email is not verified: {Email}", payload.Email);
            return null;
        }

        string normalizedEmail = NormalizeEmail(payload.Email);
        User? user = await _userRepository.GetByProviderAsync(GoogleProvider, payload.Subject)
             ?? await _userRepository.GetByEmailAsync(normalizedEmail);

if (user is null)
{
    user = await _userRepository.CreateAsync(new User
    {
        Email = normalizedEmail,
        PasswordHash = string.Empty,
        IsEmailVerified = true,
        IsMobileVerified = false,
        CreatedAtUtc = DateTime.UtcNow,
        LastLoginAtUtc = DateTime.UtcNow
    });
}
else
{
    user.IsEmailVerified = true;
    user.LastLoginAtUtc = DateTime.UtcNow;

    await _userRepository.UpdateAsync(user);
}

        User? alreadyLinkedUser = await _userRepository.GetByProviderAsync(GoogleProvider, payload.Subject);
        if (alreadyLinkedUser is null)
        {
            await _userRepository.AddProviderAsync(new AuthProvider
            {
                UserId = user.Id,
                ProviderName = GoogleProvider,
                ProviderUserId = payload.Subject,
                ProviderEmail = normalizedEmail
            });
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);
        _logger.LogInformation("User {UserId} logged in with Google.", user.Id);
        return BuildAuthResponse(user);
    }

    public async Task<bool> SendMobileOtpAsync(SendOtpDto dto)
    {
        string mobileNumber = NormalizeMobile(dto.MobileNumber) ?? string.Empty;
        string otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        OtpRequest request = new()
        {
            MobileNumber = mobileNumber,
            OtpHash = BCrypt.Net.BCrypt.HashPassword(otp),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(_configuration.GetValue<int>("Otp:ExpiryMinutes", 5))
        };

        await _otpRepository.CreateAsync(request);
        await _otpSender.SendOtpAsync(mobileNumber, otp);
        _logger.LogInformation("OTP generated for mobile login: {MobileNumber}", mobileNumber);
        return true;
    }

    public async Task<AuthResponseDto?> VerifyMobileOtpAsync(VerifyOtpDto dto)
    {
        string mobileNumber = NormalizeMobile(dto.MobileNumber) ?? string.Empty;
        OtpRequest? otpRequest = await _otpRepository.GetLatestActiveAsync(mobileNumber);

        if (otpRequest is null)
        {
            _logger.LogWarning("OTP verification failed because no active OTP exists for {MobileNumber}.", mobileNumber);
            return null;
        }

        int maxAttempts = _configuration.GetValue<int>("Otp:MaxAttempts", 5);
        if (otpRequest.FailedAttempts >= maxAttempts)
        {
            _logger.LogWarning("OTP verification blocked after max attempts for {MobileNumber}.", mobileNumber);
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(dto.Otp, otpRequest.OtpHash))
        {
            otpRequest.FailedAttempts++;
            await _otpRepository.UpdateAsync(otpRequest);
            _logger.LogWarning("Invalid OTP attempt for {MobileNumber}.", mobileNumber);
            return null;
        }

        otpRequest.UsedAtUtc = DateTime.UtcNow;
        await _otpRepository.UpdateAsync(otpRequest);

        User? user = await _userRepository.GetByMobileNumberAsync(mobileNumber);
        if (user is null)
        {
            string syntheticEmail = $"mobile-{mobileNumber.Replace("+", string.Empty)}@local.auth";
            user = await _userRepository.CreateAsync(new User
            {
                Email = syntheticEmail,
                PasswordHash = string.Empty,
                MobileNumber = mobileNumber,
                IsMobileVerified = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else if (!user.IsMobileVerified)
        {
            user.IsMobileVerified = true;
            await _userRepository.UpdateAsync(user);
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);
        _logger.LogInformation("User {UserId} logged in with mobile OTP.", user.Id);
        return BuildAuthResponse(user);
    }
    public async Task<UserProfileDto?> GetProfileAsync(int userId)
{
    User? user = await _userRepository.GetByIdAsync(userId);

    if (user is null)
    {
        return null;
    }

    return BuildUserProfile(user);
}

public async Task<UserProfileDto?> UpdateProfileAsync(int userId, UpdateUserProfileDto dto)
{
    User? user = await _userRepository.GetByIdAsync(userId);

    if (user is null)
    {
        return null;
    }

    string normalizedEmail = NormalizeEmail(dto.Email);
    string? normalizedMobile = NormalizeMobile(dto.MobileNumber);

    if (await _userRepository.EmailExistsForOtherUserAsync(normalizedEmail, userId))
    {
        _logger.LogWarning("Profile update blocked because email already exists: {Email}", normalizedEmail);
        return null;
    }

    if (!string.IsNullOrWhiteSpace(normalizedMobile) &&
        await _userRepository.MobileNumberExistsForOtherUserAsync(normalizedMobile, userId))
    {
        _logger.LogWarning("Profile update blocked because mobile already exists: {MobileNumber}", normalizedMobile);
        return null;
    }

    user.Email = normalizedEmail;
    user.MobileNumber = normalizedMobile;

    await _userRepository.UpdateAsync(user);

    _logger.LogInformation("User {UserId} updated profile.", userId);

    return BuildUserProfile(user);
}

public async Task<bool> ChangePasswordAsync(int userId, ChangePasswordDto dto)
{
    User? user = await _userRepository.GetByIdAsync(userId);

    if (user is null)
    {
        return false;
    }

    if (string.IsNullOrWhiteSpace(user.PasswordHash))
    {
        _logger.LogWarning("Password change failed because user {UserId} has no password login.", userId);
        return false;
    }

    if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
    {
        _logger.LogWarning("Password change failed due to wrong current password for user {UserId}.", userId);
        return false;
    }

    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
    await _userRepository.UpdateAsync(user);

    _logger.LogInformation("User {UserId} changed password.", userId);

    return true;
}

private static UserProfileDto BuildUserProfile(User user) => new()
{
    UserId = user.Id,
    Email = user.Email,
    MobileNumber = user.MobileNumber,
    IsEmailVerified = user.IsEmailVerified,
    IsMobileVerified = user.IsMobileVerified,
    CreatedAtUtc = user.CreatedAtUtc,
    LastLoginAtUtc = user.LastLoginAtUtc
};

    private AuthResponseDto BuildAuthResponse(User user) => new()
    {
        Token = GenerateJwtToken(user),
        Email = user.Email,
        MobileNumber = user.MobileNumber,
        UserId = user.Id
    };

    private string GenerateJwtToken(User user)
    {
        string jwtKey = _configuration["Jwt:Key"]!;
        SigningCredentials signingCredentials = new(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            SecurityAlgorithms.HmacSha256);

        int expiryDays = _configuration.GetValue<int>("Jwt:ExpiryDays", 7);
        List<Claim> claims = new()
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("userId", user.Id.ToString())
        };

        if (!string.IsNullOrWhiteSpace(user.MobileNumber))
        {
            claims.Add(new Claim(ClaimTypes.MobilePhone, user.MobileNumber));
        }

        JwtSecurityToken token = new(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(expiryDays),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string? NormalizeMobile(string? mobileNumber)
    {
        if (string.IsNullOrWhiteSpace(mobileNumber))
        {
            return null;
        }

        string normalized = Regex.Replace(mobileNumber.Trim(), @"[\s-]", string.Empty);
        return normalized.StartsWith('+') ? normalized : $"+91{normalized}";
    }

    private static bool IsDuplicateConstraint(DbUpdateException ex)
    {
        string message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
            || message.Contains("IX_Users_Email", StringComparison.OrdinalIgnoreCase)
            || message.Contains("IX_Users_MobileNumber", StringComparison.OrdinalIgnoreCase);
    }
}
