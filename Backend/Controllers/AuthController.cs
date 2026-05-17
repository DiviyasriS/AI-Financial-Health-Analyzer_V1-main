using Microsoft.AspNetCore.Mvc;

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
}
