public interface IOtpRepository
{
    Task CreateAsync(OtpRequest request);
    Task<OtpRequest?> GetLatestActiveAsync(string mobileNumber);
    Task UpdateAsync(OtpRequest request);
}
