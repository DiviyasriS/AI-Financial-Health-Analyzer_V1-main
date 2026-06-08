using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    private readonly IWebHostEnvironment _env;

    public TestController(IWebHostEnvironment env)
    {
        _env = env;
    }

    [HttpGet("email")]
    public async Task<IActionResult> TestEmail(
        [FromServices] IEmailSender emailSender)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        await emailSender.SendEmailAsync(
            "diviyasris@karunya.edu.in",
            "SMTP Test Email",
            "Congratulations! SMTP is working successfully.");

        return Ok("Email sent successfully");
    }
}