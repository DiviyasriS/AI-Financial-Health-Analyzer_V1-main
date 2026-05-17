public interface IAuthService
{
    Task<AuthResponseDto?> RegisterAsync(RegisterDto dto);
    Task<AuthResponseDto?> LoginAsync(LoginDto dto);
    Task<AuthResponseDto?> GoogleLoginAsync(GoogleLoginDto dto);
    Task<bool> SendMobileOtpAsync(SendOtpDto dto);
    Task<AuthResponseDto?> VerifyMobileOtpAsync(VerifyOtpDto dto);
}
