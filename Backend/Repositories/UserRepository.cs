using Microsoft.EntityFrameworkCore;

// Concrete implementation of IUserRepository.
// IMPORTANT: AuthService always normalises email to lowercase BEFORE calling any method here.
// Therefore all queries use direct == comparison, which allows the DB index on Email to be used.

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<User?> GetByMobileNumberAsync(string mobileNumber)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.MobileNumber == mobileNumber);
    }

    public async Task<User?> GetByProviderAsync(string providerName, string providerUserId)
    {
        return await _context.AuthProviders
            .Where(p => p.ProviderName == providerName && p.ProviderUserId == providerUserId)
            .Select(p => p.User)
            .FirstOrDefaultAsync();
    }

    public async Task<User> CreateAsync(User user)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _context.Users.AnyAsync(u => u.Email == email);
    }

    public async Task<bool> MobileNumberExistsAsync(string mobileNumber)
    {
        return await _context.Users.AnyAsync(u => u.MobileNumber == mobileNumber);
    }

    public async Task<AuthProvider> AddProviderAsync(AuthProvider provider)
    {
        _context.AuthProviders.Add(provider);
        await _context.SaveChangesAsync();
        return provider;
    }

    public async Task UpdateAsync(User user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }
    
    public async Task<User?> GetByIdAsync(int userId)
{
    return await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
}

public async Task<bool> EmailExistsForOtherUserAsync(string email, int userId)
{
    return await _context.Users.AnyAsync(u => u.Email == email && u.Id != userId);
}

public async Task<bool> MobileNumberExistsForOtherUserAsync(string mobileNumber, int userId)
{
    return await _context.Users.AnyAsync(u => u.MobileNumber == mobileNumber && u.Id != userId);
}
}
