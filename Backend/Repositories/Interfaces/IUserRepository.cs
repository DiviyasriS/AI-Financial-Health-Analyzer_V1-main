// Interface defines WHAT the repository can do
// The controller and service only ever talk to this interface, never the concrete class
// This is what makes the code testable and swappable

public interface IUserRepository
{
    // Find a user by their email address
    Task<User?> GetByEmailAsync(string email);

    // Save a new user to the database and return the saved entity (with Id populated)
    Task<User> CreateAsync(User user);

    // Quick check — does this email already exist?
    Task<bool> EmailExistsAsync(string email);
}