namespace TravelGpt.Models
{
    public class Hotel
    {
        public int Id { get; set; }
        public int TripStopId { get; set; }
        public string Name { get; set; } = string.Empty;
        public double PricePerNight { get; set; }
        public double Rating { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }

        // Navigation property
        public virtual TripStop? TripStop { get; set; }
    }
}