public interface IOtpSender
{
    Task SendOtpAsync(string mobileNumber, string otp);
}
