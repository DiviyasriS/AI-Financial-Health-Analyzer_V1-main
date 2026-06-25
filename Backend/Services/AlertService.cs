public class AlertService
{
    private readonly IEmailSender _emailSender;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        IEmailSender emailSender,
        IUserRepository userRepository,
        ILogger<AlertService> logger)
    {
        _emailSender = emailSender;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task SendRiskAlertAsync(int userId, RiskDto risk)
    {
        // Send alert only for Medium and High risk.
        // Do not send email every time dashboard loads.
        if (risk.RiskLevel != "Medium" && risk.RiskLevel != "High")
            return;

        User? user = await _userRepository.GetByIdAsync(userId);

        if (user == null || string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning(
                "Risk alert skipped because user email was not found. UserId={UserId}",
                userId);
            return;
        }

        string subject = $"Financial Health Alert: {risk.RiskLevel} Risk Detected";

        string body =
$@"Hello,

Your AI Financial Health Analyzer detected a {risk.RiskLevel} financial risk level.

Risk Score: {(risk.RiskScore * 100):F1}%

Summary:
{risk.Description}

Please review your dashboard for detailed insights and spending suggestions.

Regards,
AI Financial Health Analyzer";

        await _emailSender.SendEmailAsync(user.Email, subject, body);

        _logger.LogInformation(
            "Risk alert sent for UserId={UserId}, RiskLevel={RiskLevel}",
            userId,
            risk.RiskLevel);
    }
}