namespace TravelGpt.Models
{
    public class TripPlan
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string UserId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? GeneratedContent { get; set; } // Contenuto generato dall'API
        public bool IsPublic { get; set; } = false;

        // Navigation properties
        public virtual AppUser? User { get; set; }
        public virtual ICollection<TripStop> Stops { get; set; } = new List<TripStop>();
        public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    }
}