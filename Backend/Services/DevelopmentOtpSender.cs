public class DevelopmentOtpSender : IOtpSender
{
    private readonly ILogger<DevelopmentOtpSender> _logger;

    public DevelopmentOtpSender(ILogger<DevelopmentOtpSender> logger)
    {
        _logger = logger;
    }

    public Task SendOtpAsync(string mobileNumber, string otp)
    {
        _logger.LogWarning("Development OTP generated for {mobileNumber}. Replace DevelopmentOtpSender with SMS provider in production.", mobileNumber);
        return Task.CompletedTask;
    }
}
