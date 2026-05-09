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
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);
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
            .AnyAsync(u => u.Email == email);
    }
}