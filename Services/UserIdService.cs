using Microsoft.EntityFrameworkCore;

namespace JobRecruitment.Services;

public interface IUserIdService
{
    Task<string> GenerateNextUserId(string userType);
}

public class UserIdService : IUserIdService
{
    private readonly DB _context;

    public UserIdService(DB context)
    {
        _context = context;
    }

    public async Task<string> GenerateNextUserId(string userType)
    {
        // FIXED: Removed transaction handling - will participate in existing transaction from controller
        try
        {
            var counter = await _context.UserIdCounters
                .FirstOrDefaultAsync(c => c.UserType == userType);

            if (counter == null)
            {
                // Initialize based on existing data
                int startNumber = await GetCurrentMaxNumber(userType);
                counter = new UserIdCounter
                {
                    UserType = userType,
                    LastNumber = startNumber
                };
                _context.UserIdCounters.Add(counter);
            }

            counter.LastNumber++;

            // FIXED: Save changes will be handled by the parent transaction
            await _context.SaveChangesAsync();

            // Generate formatted ID
            string prefix = userType == "Employer" ? "EMP" : "SEEK";
            string format = userType == "Employer" ? "D7" : "D6";
            return prefix + counter.LastNumber.ToString(format);
        }
        catch
        {
            // FIXED: Let exceptions bubble up to the controller's transaction handler
            throw;
        }
    }

    private async Task<int> GetCurrentMaxNumber(string userType)
    {
        string prefix = userType == "Employer" ? "EMP" : "SEEK";

        var lastProfile = await _context.UserProfiles
            .Where(up => up.GeneratedUserId.StartsWith(prefix))
            .OrderByDescending(up => up.GeneratedUserId)
            .FirstOrDefaultAsync();

        if (lastProfile != null)
        {
            string numberPart = lastProfile.GeneratedUserId.Replace(prefix, "");
            if (int.TryParse(numberPart, out int number))
                return number;
        }

        return 0;
    }
}