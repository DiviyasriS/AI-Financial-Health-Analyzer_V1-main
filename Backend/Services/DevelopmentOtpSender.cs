public class DevelopmentOtpSender : IOtpSender
{
    private readonly ILogger<DevelopmentOtpSender> _logger;

    public DevelopmentOtpSender(ILogger<DevelopmentOtpSender> logger)
    {
        _logger = logger;
    }

    public Task SendOtpAsync(string mobileNumber, string otp)
    {
        _logger.LogWarning("Development OTP for {MobileNumber}: {Otp}. Replace DevelopmentOtpSender with SMS provider in production.", mobileNumber, otp);
        return Task.CompletedTask;
    }
}
