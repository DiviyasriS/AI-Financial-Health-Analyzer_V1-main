using Microsoft.AspNetCore.Mvc;

// AuthController is the entry point for all authentication requests
// It is intentionally thin — all logic lives in AuthService
// Controller's job: receive request → validate input → call service → return response

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    // ─── POST /api/auth/register ─────────────────────────────────────────────

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        // Basic input validation before calling the service
        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { message = "Email is required." });

        if (string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Password is required." });

        if (dto.Password.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });

        var result = await _authService.RegisterAsync(dto);

        // Service returns null when email is already taken
        if (result == null)
            return Conflict(new { message = "An account with this email already exists." });

        return Ok(result);
    }

    // ─── POST /api/auth/login ────────────────────────────────────────────────

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { message = "Email is required." });

        if (string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Password is required." });

        var result = await _authService.LoginAsync(dto);

        // Service returns null when credentials are wrong
        if (result == null)
            return Unauthorized(new { message = "Invalid email or password." });

        return Ok(result);
    }
}