using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelGpt.Data;
using TravelGpt.Models;
using TravelGpt.Services;

namespace TravelGpt.Pages.Itinerary
{
    [Authorize]
    public class ListModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ItineraryService _itineraryService;

        public ListModel(ApplicationDbContext context, ItineraryService itineraryService)
        {
            _context = context;
            _itineraryService = itineraryService;
        }

        public List<TripPlan> TripPlans { get; set; } = new List<TripPlan>();
        public Dictionary<int, List<string>> TripCities { get; set; } = new Dictionary<int, List<string>>();
        public Dictionary<int, string> TripImages { get; set; } = new Dictionary<int, string>();

        public async Task OnGetAsync()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId != null)
            {
                TripPlans = await _context.TripPlans
                    .Where(t => t.UserId == userId)
                    .OrderByDescending(t => t.CreatedAt)
                    .ToListAsync();

                foreach (var trip in TripPlans)
                {
                    // Get cities for each trip
                    var stops = await _context.TripStops
                        .Where(s => s.TripPlanId == trip.Id)
                        .OrderBy(s => s.Order)
                        .Select(s => s.CityName)
                        .ToListAsync();

                    TripCities[trip.Id] = stops;

                    // Try to get a representative image for the trip
                    var firstStop = await _context.TripStops
                        .Where(s => s.TripPlanId == trip.Id)
                        .OrderBy(s => s.Order)
                        .Include(s => s.Hotel)
                        .FirstOrDefaultAsync();

                    if (firstStop != null)
                    {
                        // Use hotel image if available
                        if (!string.IsNullOrEmpty(firstStop.Hotel?.ImageUrl))
                        {
                            TripImages[trip.Id] = firstStop.Hotel.ImageUrl;
                        }
                        // Otherwise use an image based on the city name from Unsplash
                        else
                        {
                            TripImages[trip.Id] = $"https://source.unsplash.com/featured/?{Uri.EscapeDataString(firstStop.CityName)}";
                        }
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var tripPlan = await _context.TripPlans.FindAsync(id);

            if (tripPlan == null)
            {
                return NotFound();
            }

            if (tripPlan.UserId != userId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            await _itineraryService.DeleteTripPlanAsync(id);

            return RedirectToPage();
        }
    }
}