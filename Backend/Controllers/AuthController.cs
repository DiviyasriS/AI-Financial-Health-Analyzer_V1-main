using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        _logger.LogInformation("Register attempt for email: {Email}", dto.Email);
        AuthResponseDto? result = await _authService.RegisterAsync(dto);

        if (result is null)
        {
            return Conflict(ApiResponse<object>.Fail("An account with this email or mobile number already exists."));
        }

        return Ok(ApiResponse<AuthResponseDto>.Ok(result, "Registration successful."));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        _logger.LogInformation("Login attempt for email: {Email}", dto.Email);
        AuthResponseDto? result = await _authService.LoginAsync(dto);

        if (result is null)
        {
            return Unauthorized(ApiResponse<object>.Fail("Invalid email or password."));
        }

        return Ok(ApiResponse<AuthResponseDto>.Ok(result, "Login successful."));
    }

    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginDto dto)
    {
        AuthResponseDto? result = await _authService.GoogleLoginAsync(dto);

        if (result is null)
        {
            return Unauthorized(ApiResponse<object>.Fail("Google Sign-In failed."));
        }

        return Ok(ApiResponse<AuthResponseDto>.Ok(result, "Google Sign-In successful."));
    }

    [HttpPost("otp/send")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpDto dto)
    {
        await _authService.SendMobileOtpAsync(dto);
        return Ok(ApiResponse<object>.Ok(null, "OTP sent successfully."));
    }

    [HttpPost("otp/verify")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto dto)
    {
        AuthResponseDto? result = await _authService.VerifyMobileOtpAsync(dto);

        if (result is null)
        {
            return Unauthorized(ApiResponse<object>.Fail("Invalid or expired OTP."));
        }

        return Ok(ApiResponse<AuthResponseDto>.Ok(result, "Mobile login successful."));
    }

    [Authorize]
[HttpGet("profile")]
public async Task<IActionResult> GetProfile()
{
    int? userId = GetCurrentUserId();

    if (userId is null)
    {
        return Unauthorized(ApiResponse<object>.Fail("Invalid user token."));
    }

    UserProfileDto? profile = await _authService.GetProfileAsync(userId.Value);

    if (profile is null)
    {
        return NotFound(ApiResponse<object>.Fail("User profile not found."));
    }

    return Ok(ApiResponse<UserProfileDto>.Ok(profile, "Profile fetched successfully."));
}

[Authorize]
[HttpPut("profile")]
public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserProfileDto dto)
{
    int? userId = GetCurrentUserId();

    if (userId is null)
    {
        return Unauthorized(ApiResponse<object>.Fail("Invalid user token."));
    }

    UserProfileDto? updatedProfile = await _authService.UpdateProfileAsync(userId.Value, dto);

    if (updatedProfile is null)
    {
        return Conflict(ApiResponse<object>.Fail("Email or mobile number already exists."));
    }

    return Ok(ApiResponse<UserProfileDto>.Ok(updatedProfile, "Profile updated successfully."));
}

[Authorize]
[HttpPut("change-password")]
public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
{
    int? userId = GetCurrentUserId();

    if (userId is null)
    {
        return Unauthorized(ApiResponse<object>.Fail("Invalid user token."));
    }

    bool changed = await _authService.ChangePasswordAsync(userId.Value, dto);

    if (!changed)
    {
        return BadRequest(ApiResponse<object>.Fail("Current password is incorrect or password change is not allowed."));
    }

    return Ok(ApiResponse<object>.Ok(null, "Password changed successfully."));
}

private int? GetCurrentUserId()
{
    string? userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("userId");

    return int.TryParse(userIdValue, out int userId) ? userId : null;
}
}
