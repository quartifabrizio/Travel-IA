using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelGpt.Data;
using TravelGpt.Models;
using TravelGpt.Services;

namespace TravelGpt.Pages.Itinerary
{
    [Authorize]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ItineraryService _itineraryService;
        private readonly GeminiService _geminiService;

        public EditModel(ApplicationDbContext context, ItineraryService itineraryService, GeminiService geminiService)
        {
            _context = context;
            _itineraryService = itineraryService;
            _geminiService = geminiService;
        }

        [BindProperty]
        public TripPlan TripPlan { get; set; } = default!;

        [BindProperty]
        public List<TripStop> TripStops { get; set; } = new List<TripStop>();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var tripPlan = await _itineraryService.GetTripPlanAsync(id, true);

            if (tripPlan == null)
            {
                return NotFound();
            }

            // Verifica che l'utente sia autorizzato a modificare questo itinerario
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (tripPlan.UserId != userId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            TripPlan = tripPlan;
            TripStops = tripPlan.Stops.OrderBy(s => s.Order).ToList();

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Verifica autorizzazione
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var existingTrip = await _context.TripPlans.FindAsync(TripPlan.Id);

            if (existingTrip == null)
            {
                return NotFound();
            }

            if (existingTrip.UserId != userId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            // Verifica che ci sia almeno una tappa
            if (TripStops == null || !TripStops.Any())
            {
                ModelState.AddModelError(string.Empty, "L'itinerario deve avere almeno una tappa.");
                return Page();
            }

            // Verifica che EndDate sia >= StartDate
            if (TripPlan.EndDate < TripPlan.StartDate)
            {
                ModelState.AddModelError(nameof(TripPlan.EndDate), "La data di fine deve essere successiva o uguale alla data di inizio.");
                return Page();
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Aggiornamento dell'itinerario principale
            existingTrip.Title = TripPlan.Title;
            existingTrip.Description = TripPlan.Description;
            existingTrip.StartDate = TripPlan.StartDate;
            existingTrip.EndDate = TripPlan.EndDate;
            existingTrip.IsPublic = TripPlan.IsPublic;

            // Aggiornamento delle tappe
            // Prima rimuovi le tappe esistenti (per semplificare)
            var existingStops = _context.TripStops.Where(s => s.TripPlanId == TripPlan.Id).ToList();
            _context.TripStops.RemoveRange(existingStops);
            await _context.SaveChangesAsync();

            // Poi aggiungi le tappe aggiornate
            for (int i = 0; i < TripStops.Count; i++)
            {
                var stop = TripStops[i];
                stop.TripPlanId = TripPlan.Id;
                stop.Order = i;

                if (stop.Hotel != null)
                {
                    stop.Hotel.TripStopId = 0; // Sarà assegnato da EF Core
                }

                if (stop.Activities != null)
                {
                    foreach (var activity in stop.Activities)
                    {
                        activity.TripStopId = 0; // Sarà assegnato da EF Core
                    }
                }

                _context.TripStops.Add(stop);
            }

            // Rigenera il contenuto dell'itinerario
            try
            {
                existingTrip.GeneratedContent = await _geminiService.GenerateItineraryContentAsync(existingTrip);
            }
            catch (Exception ex)
            {
                // In caso di errore, mantieni il contenuto precedente
                // Ma continua a salvare le altre modifiche
            }

            await _context.SaveChangesAsync();

            return RedirectToPage("./Details", new { id = TripPlan.Id });
        }
    }
}