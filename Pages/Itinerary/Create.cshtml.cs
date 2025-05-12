using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelGpt.Data;
using TravelGpt.Models;
using TravelGpt.Services;

namespace TravelGpt.Pages.Itinerary
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ItineraryService _itineraryService;
        private readonly GeminiService _geminiService;

        public CreateModel(ApplicationDbContext context, ItineraryService itineraryService, GeminiService geminiService)
        {
            _context = context;
            _itineraryService = itineraryService;
            _geminiService = geminiService;
        }

        [BindProperty]
        public TripPlan TripPlan { get; set; } = new TripPlan
        {
            StartDate = DateTime.Today.AddDays(7),
            EndDate = DateTime.Today.AddDays(10),
            IsPublic = false
        };

        [BindProperty]
        public TripStop FirstStop { get; set; } = new TripStop
        {
            Days = 1
        };

        public IActionResult OnGet()
        {
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Verifica che EndDate sia >= StartDate
            if (TripPlan.EndDate < TripPlan.StartDate)
            {
                ModelState.AddModelError(nameof(TripPlan.EndDate), "La data di fine deve essere successiva o uguale alla data di inizio.");
                return Page();
            }

            // Aggiungi l'userId corrente
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Forbid();
            }
            TripPlan.UserId = userId;
            TripPlan.CreatedAt = DateTime.Now;

            // Crea l'itinerario
            _context.TripPlans.Add(TripPlan);
            await _context.SaveChangesAsync();

            // Aggiungi la prima tappa
            FirstStop.TripPlanId = TripPlan.Id;
            FirstStop.Order = 0;

            // Se l'utente non ha fornito coordinate, prova a ricavarle
            if (FirstStop.Latitude == 0 && FirstStop.Longitude == 0)
            {
                try
                {
                    var locationDetails = await _geminiService.GetLocationDetailsAsync(FirstStop.CityName);
                    if (locationDetails != null)
                    {
                        FirstStop.Latitude = locationDetails.Latitude;
                        FirstStop.Longitude = locationDetails.Longitude;

                        // Crea hotel se ci sono suggerimenti
                        if (locationDetails.Hotels.Any())
                        {
                            var suggestedHotel = locationDetails.Hotels.First();
                            FirstStop.Hotel = new Hotel
                            {
                                Name = suggestedHotel.Name,
                                PricePerNight = suggestedHotel.PricePerNight,
                                Rating = suggestedHotel.Rating,
                                Description = suggestedHotel.Description
                            };
                        }

                        // Aggiunge alcune attività suggerite
                        if (locationDetails.Activities.Any())
                        {
                            FirstStop.Activities = locationDetails.Activities.Take(3).Select(a => new Activity
                            {
                                Name = a.Name,
                                Description = a.Description,
                                Price = 0 // Prezzo non disponibile nell'API, default a 0
                            }).ToList();
                        }
                    }
                }
                catch (Exception)
                {
                    // In caso di errore usa valori di default
                    FirstStop.Latitude = 0;
                    FirstStop.Longitude = 0;
                }
            }

            _context.TripStops.Add(FirstStop);
            await _context.SaveChangesAsync();

            // Genera il contenuto dell'itinerario
            try
            {
                TripPlan.GeneratedContent = await _geminiService.GenerateItineraryContentAsync(TripPlan);
                await _context.SaveChangesAsync();
            }
            catch (Exception)
            {
                // Ignora eventuali errori nella generazione del contenuto
            }

            return RedirectToPage("./Details", new { id = TripPlan.Id });
        }
    }
}
