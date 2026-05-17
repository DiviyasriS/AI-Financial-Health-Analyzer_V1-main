using System.ComponentModel.DataAnnotations;

public class RegisterDto
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
    public string Password { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Invalid mobile number.")]
    public string? MobileNumber { get; set; }
}

public class LoginDto
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; set; } = string.Empty;
}

public class GoogleLoginDto
{
    [Required(ErrorMessage = "Google credential is required.")]
    public string Credential { get; set; } = string.Empty;
}

public class SendOtpDto
{
    [Required(ErrorMessage = "Mobile number is required.")]
    [RegularExpression(@"^\+?[1-9]\d{9,14}$", ErrorMessage = "Enter a valid mobile number with country code.")]
    public string MobileNumber { get; set; } = string.Empty;
}

public class VerifyOtpDto
{
    [Required(ErrorMessage = "Mobile number is required.")]
    [RegularExpression(@"^\+?[1-9]\d{9,14}$", ErrorMessage = "Enter a valid mobile number with country code.")]
    public string MobileNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "OTP is required.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "OTP must be 6 digits.")]
    public string Otp { get; set; } = string.Empty;
}

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? MobileNumber { get; set; }
    public int UserId { get; set; }
}
