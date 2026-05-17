using Microsoft.EntityFrameworkCore;

public class OtpRepository : IOtpRepository
{
    private readonly AppDbContext _context;

    public OtpRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task CreateAsync(OtpRequest request)
    {
        _context.OtpRequests.Add(request);
        await _context.SaveChangesAsync();
    }

    public async Task<OtpRequest?> GetLatestActiveAsync(string mobileNumber)
    {
        return await _context.OtpRequests
            .Where(o => o.MobileNumber == mobileNumber && o.UsedAtUtc == null && o.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync();
    }

    public async Task UpdateAsync(OtpRequest request)
    {
        _context.OtpRequests.Update(request);
        await _context.SaveChangesAsync();
    }
}
