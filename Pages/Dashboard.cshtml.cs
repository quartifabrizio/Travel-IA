using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelGpt.Data;
using TravelGpt.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;

namespace TravelGpt.Pages
{
    [Authorize]
    public class DashboardModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public DashboardModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<DestinationViewModel> PopularDestinations { get; set; } = new();
        public List<TripPlanViewModel> RecentTrips { get; set; } = new();

        public async Task OnGetAsync()
        {
            PopularDestinations = GetMockDestinations();

            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(currentUserId))
            {
                var recentTrips = await _context.TripPlans
                    .Where(t => t.UserId == currentUserId)
                    .OrderByDescending(t => t.CreatedAt)
                    .Take(3)
                    .ToListAsync();

                RecentTrips = recentTrips.Select(t => new TripPlanViewModel
                {
                    Id = t.Id,
                    Title = t.Title,
                    Description = t.Description != null && t.Description.Length > 100
                                  ? t.Description.Substring(0, 100) + "..."
                                  : t.Description ?? "Nessuna descrizione",
                    StartDate = t.StartDate,
                    EndDate = t.EndDate,
                    Cities = GetCitiesFromTrip(t)
                }).ToList();
            }
        }

        private List<string> GetCitiesFromTrip(TripPlan trip)
        {
            var cities = new HashSet<string>();

            // Get cities from stops
            var stops = _context.TripStops
                .Where(s => s.TripPlanId == trip.Id)
                .OrderBy(s => s.Order)
                .Select(s => s.CityName)
                .ToList();

            if (stops.Any())
            {
                return stops;
            }

            // Fallback: try to extract from generated content
            if (!string.IsNullOrEmpty(trip.GeneratedContent))
            {
                var lines = trip.GeneratedContent.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("Giorno") && line.Contains(':'))
                    {
                        var contentAfterColon = line.Substring(line.IndexOf(':') + 1).Trim();
                        var potentialCity = contentAfterColon.Split(new[] { '-', ',', '.' }, 2)[0].Trim();
                        if (!string.IsNullOrWhiteSpace(potentialCity) && potentialCity.Length < 30 && !potentialCity.Contains("http"))
                        {
                            cities.Add(potentialCity);
                        }
                    }
                }
            }

            // Final fallback
            if (!cities.Any() && !string.IsNullOrWhiteSpace(trip.Title))
            {
                cities.Add(trip.Title.Split('-')[0].Trim());
            }
            if (!cities.Any())
            {
                cities.Add("Destinazione Sconosciuta");
            }

            return cities.ToList();
        }

        // Mock Data for Popular Destinations
        private List<DestinationViewModel> GetMockDestinations()
        {
            return new List<DestinationViewModel>
            {
                new DestinationViewModel { Id = 1, Name = "Roma", Country = "Italia", ShortDescription = "Esplora il Colosseo, il Vaticano e gusta la pasta autentica.", ImageUrl = "https://images.unsplash.com/photo-1552832230-c0197dd311b5?ixlib=rb-4.0.3&ixid=M3wxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8fA%3D%3D&auto=format&fit=crop&w=796&q=80", Rating = 4.8, Category = "city", MinPrice = 89, Latitude = 41.9028, Longitude = 12.4964, PopularityIndex = 1 },
                new DestinationViewModel { Id = 2, Name = "Parigi", Country = "Francia", ShortDescription = "Ammira la Torre Eiffel, visita il Louvre e passeggia lungo la Senna.", ImageUrl = "https://images.unsplash.com/photo-1502602898657-3e91760c0341?ixlib=rb-4.0.3&ixid=M3wxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8fA%3D%3D&auto=format&fit=crop&w=1170&q=80", Rating = 4.7, Category = "city", MinPrice = 95, Latitude = 48.8566, Longitude = 2.3522, PopularityIndex = 2 },
                new DestinationViewModel { Id = 3, Name = "Santorini", Country = "Grecia", ShortDescription = "Goditi tramonti mozzafiato, spiagge vulcaniche e villaggi bianchi.", ImageUrl = "https://images.unsplash.com/photo-1570177381201-27111f7a4c6a?ixlib=rb-4.0.3&ixid=M3wxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8fA%3D%3D&auto=format&fit=crop&w=1170&q=80", Rating = 4.9, Category = "beach", MinPrice = 120, Latitude = 36.3932, Longitude = 25.4615, PopularityIndex = 5 },
                new DestinationViewModel { Id = 4, Name = "Kyoto", Country = "Giappone", ShortDescription = "Immergiti nella cultura tradizionale, tra templi antichi e giardini zen.", ImageUrl = "https://images.unsplash.com/photo-1545569341-9eb8b30979d9?ixlib=rb-4.0.3&ixid=M3wxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8fA%3D%3D&auto=format&fit=crop&w=1170&q=80", Rating = 4.8, Category = "cultural", MinPrice = 140, Latitude = 35.0116, Longitude = 135.7681, PopularityIndex = 4 },
                new DestinationViewModel { Id = 5, Name = "Interlaken", Country = "Svizzera", ShortDescription = "Avventura tra le Alpi, ideale per escursioni, sci e sport estremi.", ImageUrl = "https://images.unsplash.com/photo-1598091383021-15914c3733d9?ixlib=rb-4.0.3&ixid=M3wxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8fA%3D%3D&auto=format&fit=crop&w=1074&q=80", Rating = 4.7, Category = "mountain", MinPrice = 115, Latitude = 46.6863, Longitude = 7.8632, PopularityIndex = 6 },
                new DestinationViewModel { Id = 6, Name = "Maldive", Country = "Maldive", ShortDescription = "Rilassati in resort di lusso su acque cristalline e spiagge bianche.", ImageUrl = "https://images.unsplash.com/photo-1516815248394-f89b86596465?ixlib=rb-4.0.3&ixid=M3wxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8fA%3D%3D&auto=format&fit=crop&w=1170&q=80", Rating = 4.9, Category = "beach", MinPrice = 250, Latitude = 3.2028, Longitude = 73.2207, PopularityIndex = 3 },
            };
        }
    }

    public class DestinationViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public double Rating { get; set; }
        public string Category { get; set; } = string.Empty;
        public int MinPrice { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int PopularityIndex { get; set; }
    }

    public class TripPlanViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<string> Cities { get; set; } = new();
    }
}