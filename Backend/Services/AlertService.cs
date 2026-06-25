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
        User? user = await _userRepository.GetByIdAsync(userId);

        if (user == null || string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning(
                "Risk email skipped because user email was not found. UserId={UserId}",
                userId);

            return;
        }

        string subject = $"Financial Health Report: {risk.RiskLevel} Risk";

        string body =
$@"Hello,

Your transaction history has been analyzed successfully.

Financial Risk Level: {risk.RiskLevel}
Risk Score: {(risk.RiskScore * 100):F1}%

Summary:
{risk.Description}

Risk Factors:
{FormatList(risk.RiskFactors)}

Positive Signals:
{FormatList(risk.PositiveSignals)}

Please check your dashboard for complete insights.

Regards,
AI Financial Health Analyzer";

        await _emailSender.SendEmailAsync(user.Email, subject, body);

        _logger.LogInformation(
            "Financial risk email sent for UserId={UserId}, RiskLevel={RiskLevel}",
            userId,
            risk.RiskLevel);
    }

    private static string FormatList(List<string>? items)
    {
        if (items == null || items.Count == 0)
            return "- None";

        return string.Join(Environment.NewLine, items.Select(i => $"- {i}"));
    }
}