using Microsoft.AspNetCore.Identity;

namespace AIDataPlatform.Data
{
    // Add profile data for application users by adding properties to the ApplicationUser class
    public class ApplicationUser : IdentityUser
    {
        // Custom property for multi tenancy
        public string? TenantId { get; set; } // Add this property
    }

}
