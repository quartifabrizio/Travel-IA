using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TravelGpt.Models
{
    public class ExpenseParticipant
    {
        [Key]
        public int Id { get; set; }

        public int ExpenseId { get; set; }

        [ForeignKey("ExpenseId")]
        public Expense Expense { get; set; }

        public int ParticipantId { get; set; }

        [ForeignKey("ParticipantId")]
        public Participants Participant { get; set; }

        // Percentuale o importo specifico se non è diviso equamente
        public decimal? CustomAmount { get; set; }

        public decimal? CustomPercentage { get; set; }
    }
}