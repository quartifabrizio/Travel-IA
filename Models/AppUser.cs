using Microsoft.AspNetCore.Identity;

namespace TravelGpt.Models
{
    public class AppUser : IdentityUser
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string ProfilePictureUrl { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation properties
        public virtual ICollection<TripPlan> TripPlans { get; set; } = new List<TripPlan>();
    }
}