using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TravelGpt.Models
{
    public class Participants
    {
        [Key]
        public int Id { get; set; }

        public int TripPlanId { get; set; }

        [ForeignKey("TripPlanId")]
        public TripPlan TripPlan { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; }

        public string Email { get; set; }

        public string ProfileImageUrl { get; set; }

        public bool IsPrimary { get; set; }

        public virtual ICollection<ExpenseParticipant> ExpenseParticipants { get; set; } = new List<ExpenseParticipant>();
    }
}