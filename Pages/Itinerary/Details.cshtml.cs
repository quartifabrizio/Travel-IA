using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelGpt.Data;
using TravelGpt.Models;
using TravelGpt.Services;

namespace TravelGpt.Pages.Itinerary
{
    [Authorize]
    public class DetailsModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ItineraryService _itineraryService;

        public DetailsModel(ApplicationDbContext context, ItineraryService itineraryService)
        {
            _context = context;
            _itineraryService = itineraryService;
        }

        public TripPlan TripPlan { get; set; } = default!;
        public int TotalStops { get; set; }
        public int TripDuration { get; set; }
        public double TotalDistance { get; set; }
        public double TotalCost { get; set; }
        public double LodgingCost { get; set; }
        public double ActivitiesCost { get; set; }
        public double ExpensesCost { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var tripPlan = await _itineraryService.GetTripPlanAsync(id, true);

            if (tripPlan == null)
            {
                return NotFound();
            }

            // Verifica l'autorizzazione (solo l'utente proprietario può vedere)
            if (tripPlan.UserId != User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            TripPlan = tripPlan;
            TotalStops = tripPlan.Stops.Count;
            TripDuration = (tripPlan.EndDate - tripPlan.StartDate).Days + 1;

            // Calcola statistiche
            var summary = await _itineraryService.CalculateTripSummaryAsync(id);
            TotalDistance = summary.ContainsKey("TotalDistance") ? summary["TotalDistance"] : 0;
            TotalCost = summary.ContainsKey("TotalCost") ? summary["TotalCost"] : 0;
            LodgingCost = summary.ContainsKey("LodgingCost") ? summary["LodgingCost"] : 0;
            ActivitiesCost = summary.ContainsKey("ActivitiesCost") ? summary["ActivitiesCost"] : 0;
            ExpensesCost = summary.ContainsKey("ExpensesCost") ? summary["ExpensesCost"] : 0;

            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var tripPlan = await _itineraryService.GetTripPlanAsync(id);

            if (tripPlan == null)
            {
                return NotFound();
            }

            // Verifica l'autorizzazione (solo l'utente proprietario può eliminare)
            if (tripPlan.UserId != User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            await _itineraryService.DeleteTripPlanAsync(id);

            return RedirectToPage("./List");
        }
    }
}