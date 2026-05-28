public interface IUserRepository
{
    Task<User?> GetByIdAsync(int userId);
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByMobileNumberAsync(string mobileNumber);
    Task<User?> GetByProviderAsync(string providerName, string providerUserId);
    Task<User> CreateAsync(User user);
    Task<bool> EmailExistsAsync(string email);
    Task<bool> EmailExistsForOtherUserAsync(string email, int userId);
    Task<bool> MobileNumberExistsAsync(string mobileNumber);
    Task<bool> MobileNumberExistsForOtherUserAsync(string mobileNumber, int userId);
    Task<AuthProvider> AddProviderAsync(AuthProvider provider);
    Task UpdateAsync(User user);
}