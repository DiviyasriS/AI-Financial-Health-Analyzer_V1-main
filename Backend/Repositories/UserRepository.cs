using Microsoft.EntityFrameworkCore;

// Concrete implementation of IUserRepository
// This is the ONLY place that talks to the database for User operations
// All database queries for users live here — nowhere else

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    // AppDbContext is injected by .NET's DI container — we never create it manually
    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        // FirstOrDefaultAsync returns null if not found — that's intentional
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
    }

    public async Task<User> CreateAsync(User user)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // After SaveChangesAsync, EF Core populates user.Id automatically
        return user;
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _context.Users
            .AnyAsync(u => u.Email.ToLower() == email.ToLower());
    }
}