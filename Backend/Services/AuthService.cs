using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

// AuthService handles all authentication business logic
// It sits between the Controller and the Repository
// Controller → AuthService → IUserRepository → Database

public class AuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IUserRepository userRepository, IConfiguration configuration, ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _configuration = configuration;
         _logger = logger;
    }

    // ─── REGISTER ────────────────────────────────────────────────────────────

    public async Task<AuthResponseDto?> RegisterAsync(RegisterDto dto)
    {
        // Step 1: Check if email is already taken
        if (await _userRepository.EmailExistsAsync(dto.Email))
        {
            // Return null to signal "conflict" — controller will send 409
            return null;
        }

        // Step 2: Hash the password — NEVER store plain text passwords
        // BCrypt automatically generates a salt and bakes it into the hash
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        // Step 3: Build the User entity
        var user = new User
        {
            Email = dto.Email.ToLower().Trim(),
            PasswordHash = passwordHash
        };

        // Step 4: Save to database
        var createdUser = await _userRepository.CreateAsync(user);

        // Step 5: Generate JWT token and return it
        return new AuthResponseDto
        {
            Token = GenerateJwtToken(createdUser),
            Email = createdUser.Email,
            UserId = createdUser.Id
        };
    }

    // ─── LOGIN ───────────────────────────────────────────────────────────────

    public async Task<AuthResponseDto?> LoginAsync(LoginDto dto)
    {
        // Step 1: Find user by email
        var user = await _userRepository.GetByEmailAsync(dto.Email);

        // Step 2: User not found — return null (controller sends 401)
        // Important: we do NOT say "email not found" to the caller
        // Always say "invalid credentials" — don't reveal which field is wrong
        if (user == null)
        {
            return null;
        }

        // Step 3: Verify password against the stored hash
        // BCrypt.Verify handles the salt internally
        var passwordMatches = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);

        if (!passwordMatches)
        {
            return null;
        }

        // Step 4: Credentials valid — generate token
        return new AuthResponseDto
        {
            Token = GenerateJwtToken(user),
            Email = user.Email,
            UserId = user.Id
        };
    }

    // ─── JWT TOKEN GENERATION ────────────────────────────────────────────────

    private string GenerateJwtToken(User user)
    {
        // The secret key — loaded from config, never hardcoded here
        var keyBytes = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);
        var securityKey = new SymmetricSecurityKey(keyBytes);
        var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        // Claims are pieces of information baked into the token
        // The client can read these (they're base64 encoded, not encrypted)
        // But they cannot be tampered with — the signature will break
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("userId", user.Id.ToString())  // easy to extract on frontend
        };

        // Build the token
        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),   // token valid for 7 days
            signingCredentials: signingCredentials
        );

        // Serialize the token to a string like: xxxxx.yyyyy.zzzzz
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}