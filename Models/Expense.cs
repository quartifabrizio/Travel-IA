namespace TravelGpt.Models
{
    public class Expense
    {
        public int Id { get; set; }
        public int TripPlanId { get; set; }
        public string Description { get; set; } = string.Empty;
        public double Amount { get; set; }
        public string Category { get; set; } = string.Empty; // Alloggio, Cibo, Trasporti, Attività, Extra
        public DateTime Date { get; set; } = DateTime.Now;
        public string? PaidBy { get; set; } // UserID di chi ha pagato
        public bool IsShared { get; set; } = true; // Se la spesa è da dividere tra i partecipanti

        // Navigation property
        public virtual TripPlan? TripPlan { get; set; }
    }
}