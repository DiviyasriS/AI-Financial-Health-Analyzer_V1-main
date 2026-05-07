using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AuthResponseDto?> RegisterAsync(RegisterDto dto)
    {
        var normalizedEmail = dto.Email.ToLower().Trim();

        if (await _userRepository.EmailExistsAsync(normalizedEmail))
        {
            _logger.LogDebug("Registration blocked — email already exists: {Email}", normalizedEmail);
            return null;
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = passwordHash
        };

        var createdUser = await _userRepository.CreateAsync(user);
        _logger.LogInformation("New user created with ID {UserId}", createdUser.Id);

        return BuildAuthResponse(createdUser);
    }

    public async Task<AuthResponseDto?> LoginAsync(LoginDto dto)
    {
        var normalizedEmail = dto.Email.ToLower().Trim();
        var user = await _userRepository.GetByEmailAsync(normalizedEmail);

        if (user == null)
        {
            _logger.LogDebug("Login failed — email not found: {Email}", normalizedEmail);
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed — invalid password for email: {Email}", normalizedEmail);
            return null;
        }

        _logger.LogInformation("User {UserId} authenticated successfully", user.Id);
        return BuildAuthResponse(user);
    }

    private AuthResponseDto BuildAuthResponse(User user) => new()
    {
        Token = GenerateJwtToken(user),
        Email = user.Email,
        UserId = user.Id
    };

    private string GenerateJwtToken(User user)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);
        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);

        var expiryDays = _configuration.GetValue<int>("Jwt:ExpiryDays", 7);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("userId", user.Id.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(expiryDays),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}