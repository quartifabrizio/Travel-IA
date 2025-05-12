namespace TravelGpt.Models
{
    public class Activity
    {
        public int Id { get; set; }
        public int TripStopId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public double Price { get; set; } = 0;
        public double Rating { get; set; } = 0;
        public string? ImageUrl { get; set; }

        // Navigation property
        public virtual TripStop? TripStop { get; set; }
    }
}