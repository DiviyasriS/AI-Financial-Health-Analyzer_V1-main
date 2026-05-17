public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByMobileNumberAsync(string mobileNumber);
    Task<User?> GetByProviderAsync(string providerName, string providerUserId);
    Task<User> CreateAsync(User user);
    Task<bool> EmailExistsAsync(string email);
    Task<bool> MobileNumberExistsAsync(string mobileNumber);
    Task<AuthProvider> AddProviderAsync(AuthProvider provider);
    Task UpdateAsync(User user);
}
