using System.Diagnostics;

namespace TravelGpt.Models
{
    public class TripStop
    {
        public int Id { get; set; }
        public int TripPlanId { get; set; }
        public string CityName { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string? Description { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int Days { get; set; } = 1;
        public int Order { get; set; } // Ordine della tappa nell'itinerario

        // Navigation properties
        public virtual TripPlan? TripPlan { get; set; }
        public virtual Hotel? Hotel { get; set; }
        public virtual ICollection<Activity> Activities { get; set; } = new List<Activity>();
    }
}