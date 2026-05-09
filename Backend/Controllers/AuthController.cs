using Microsoft.AspNetCore.Mvc;

// FIX: Both endpoints now return the raw AuthResponseDto directly (not wrapped in ApiResponse).
//
// WHY: The Angular AuthService does http.post<AuthResponse>() and immediately reads
// response.token, response.email, response.userId. If the backend wraps in ApiResponse,
// the actual data is at response.data.token — which Angular's tap(saveSession) never sees,
// so it stores `undefined` as the JWT token and every subsequent request fails with 401.


[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService          _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger      = logger;
    }

    // POST /api/auth/register
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { message = "Invalid input.", errors = ModelState });

        _logger.LogInformation("Register attempt for email: {Email}", dto.Email);

        var result = await _authService.RegisterAsync(dto);

        if (result == null)
        {
            _logger.LogWarning("Registration conflict for email: {Email}", dto.Email);
            return Conflict(new { message = "An account with this email already exists." });
        }

        _logger.LogInformation("User registered successfully: {Email}", dto.Email);

        // Return the flat AuthResponseDto directly so Angular can read result.token immediately
        return Ok(result);
    }

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { message = "Invalid input." });

        _logger.LogInformation("Login attempt for email: {Email}", dto.Email);

        var result = await _authService.LoginAsync(dto);

        if (result == null)
        {
            _logger.LogWarning("Failed login attempt for email: {Email}", dto.Email);
            return Unauthorized(new { message = "Invalid email or password." });
        }

        _logger.LogInformation("User logged in successfully: {Email}", dto.Email);

        // Return the flat AuthResponseDto directly so Angular can read result.token immediately
        return Ok(result);
    }
}