using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

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
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<object>.Fail("Invalid input."));

        _logger.LogInformation("Register attempt for email: {Email}", dto.Email);

        var result = await _authService.RegisterAsync(dto);

        if (result == null)
        {
            _logger.LogWarning("Registration conflict for email: {Email}", dto.Email);
            return Conflict(ApiResponse<object>.Fail("An account with this email already exists."));
        }

        _logger.LogInformation("User registered successfully: {Email}", dto.Email);
        return Ok(ApiResponse<AuthResponseDto>.Ok(result, "Registration successful."));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<object>.Fail("Invalid input."));

        _logger.LogInformation("Login attempt for email: {Email}", dto.Email);

        var result = await _authService.LoginAsync(dto);

        if (result == null)
        {
            _logger.LogWarning("Failed login attempt for email: {Email}", dto.Email);
            return Unauthorized(ApiResponse<object>.Fail("Invalid email or password."));
        }

        _logger.LogInformation("User logged in successfully: {Email}", dto.Email);
        return Ok(ApiResponse<AuthResponseDto>.Ok(result, "Login successful."));
    }
}