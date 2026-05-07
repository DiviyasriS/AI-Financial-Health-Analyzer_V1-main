using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { message = "Email is required." });

        if (!new EmailAddressAttribute().IsValid(dto.Email))
            return BadRequest(new { message = "Email format is invalid." });

        if (string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Password is required." });

        if (dto.Password.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });

        _logger.LogInformation("Register attempt for email: {Email}", dto.Email);

        AuthResponseDto? result = await _authService.RegisterAsync(dto);

        if (result == null)
        {
            _logger.LogWarning("Registration failed — email already exists: {Email}", dto.Email);
            return Conflict(new { message = "An account with this email already exists." });
        }

        _logger.LogInformation("User registered successfully: {Email}", dto.Email);
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { message = "Email is required." });

        if (string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Password is required." });

        _logger.LogInformation("Login attempt for email: {Email}", dto.Email);

        AuthResponseDto? result = await _authService.LoginAsync(dto);

        if (result == null)
        {
            _logger.LogWarning("Login failed for email: {Email}", dto.Email);
            return Unauthorized(new { message = "Invalid email or password." });
        }

        _logger.LogInformation("Login successful for email: {Email}", dto.Email);
        return Ok(result);
    }
}