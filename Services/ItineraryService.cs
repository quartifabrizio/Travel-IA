using Microsoft.EntityFrameworkCore;
using TravelGpt.Data;
using TravelGpt.Models;

namespace TravelGpt.Services
{
    public class ItineraryService
    {
        private readonly ApplicationDbContext _context;
        private readonly GeminiService _geminiService;
        private readonly ILogger<ItineraryService> _logger;

        public ItineraryService(ApplicationDbContext context, GeminiService geminiService, ILogger<ItineraryService> logger)
        {
            _context = context;
            _geminiService = geminiService;
            _logger = logger;
        }

        public async Task<TripPlan?> GetTripPlanAsync(int id, bool includeDetails = false)
        {
            if (includeDetails)
            {
                return await _context.TripPlans
                    .Include(t => t.Stops.OrderBy(s => s.Order))
                        .ThenInclude(s => s.Hotel)
                    .Include(t => t.Stops)
                        .ThenInclude(s => s.Activities)
                    .Include(t => t.Expenses)
                    .FirstOrDefaultAsync(t => t.Id == id);
            }
            else
            {
                return await _context.TripPlans
                    .FirstOrDefaultAsync(t => t.Id == id);
            }
        }

        public async Task<List<TripPlan>> GetUserTripsAsync(string userId)
        {
            return await _context.TripPlans
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<int> SaveTripPlanAsync(TripPlan tripPlan)
        {
            if (tripPlan.Id == 0) // New trip
            {
                tripPlan.CreatedAt = DateTime.Now;
                _context.TripPlans.Add(tripPlan);
            }
            else // Update existing trip
            {
                _context.Update(tripPlan);
            }

            // Generate itinerary content if needed
            if (string.IsNullOrEmpty(tripPlan.GeneratedContent) && tripPlan.Stops.Any())
            {
                try
                {
                    tripPlan.GeneratedContent = await _geminiService.GenerateItineraryContentAsync(tripPlan);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate itinerary content for trip {TripId}", tripPlan.Id);
                    // Continue saving even if content generation fails
                }
            }

            await _context.SaveChangesAsync();
            return tripPlan.Id;
        }

        public async Task DeleteTripPlanAsync(int id)
        {
            var tripPlan = await _context.TripPlans.FindAsync(id);
            if (tripPlan != null)
            {
                _context.TripPlans.Remove(tripPlan);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<TripPlan>> GetRecentPublicTripsAsync(int count = 5)
        {
            return await _context.TripPlans
                .Where(t => t.IsPublic)
                .OrderByDescending(t => t.CreatedAt)
                .Take(count)
                .ToListAsync();
        }

        public async Task<Dictionary<string, double>> CalculateTripSummaryAsync(int tripId)
        {
            var trip = await _context.TripPlans
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Hotel)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Activities)
                .Include(t => t.Expenses)
                .FirstOrDefaultAsync(t => t.Id == tripId);

            if (trip == null)
            {
                return new Dictionary<string, double>();
            }

            var summary = new Dictionary<string, double>
            {
                { "TotalDays", (trip.EndDate - trip.StartDate).Days + 1 },
                { "TotalDistance", CalculateTotalDistance(trip.Stops.OrderBy(s => s.Order).ToList()) },
                { "LodgingCost", trip.Stops.Sum(s => (s.Hotel?.PricePerNight ?? 0) * s.Days) },
                { "ActivitiesCost", trip.Stops.SelectMany(s => s.Activities).Sum(a => a.Price) },
                { "ExpensesCost", trip.Expenses.Sum(e => e.Amount) },
                { "TotalCost", 0 } // Will be calculated below
            };

            // Calculate total cost
            summary["TotalCost"] = summary["LodgingCost"] + summary["ActivitiesCost"] + summary["ExpensesCost"];

            return summary;
        }

        private double CalculateTotalDistance(List<TripStop> stops)
        {
            double totalDistance = 0;
            if (stops.Count < 2) return totalDistance;

            for (int i = 0; i < stops.Count - 1; i++)
            {
                totalDistance += CalculateDistance(
                    stops[i].Latitude, stops[i].Longitude,
                    stops[i + 1].Latitude, stops[i + 1].Longitude
                );
            }

            return totalDistance;
        }

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            // Haversine formula to calculate distance between two points on Earth
            const double R = 6371; // Earth radius in km
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180;
        }
    }
}