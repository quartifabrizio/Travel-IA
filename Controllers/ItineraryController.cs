using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TravelGpt.Data;
using TravelGpt.Models;
using TravelGpt.Services;

namespace TravelGpt.Controllers
{
    [Authorize]
    public class ItineraryController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<AppUser> _userManager; // Modificato qui
        private readonly GeminiService _geminiService;
        private readonly ILogger<ItineraryController> _logger;

        public ItineraryController(
            ApplicationDbContext context,
            UserManager<AppUser> userManager, // Modificato qui
            GeminiService geminiService,
            ILogger<ItineraryController> logger)
        {
            _context = context;
            _userManager = userManager;
            _geminiService = geminiService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var trips = await _context.TripPlans
                .Where(t => t.UserId == currentUser.Id)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return View(trips);
        }

        [HttpPost]
        [Route("Itinerary/Create")]
        public async Task<IActionResult> Create([FromBody] TripPlanCreateViewModel model)
        {
            try
            {
                _logger.LogInformation("Ricevuta richiesta creazione itinerario");

                if (model == null || model.stops == null || model.stops.Count == 0)
                {
                    _logger.LogWarning("Dati itinerario incompleti");
                    return BadRequest(new { success = false, message = "Dati itinerario incompleti" });
                }

                // Get current user
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                {
                    _logger.LogWarning("Tentativo non autorizzato di creare itinerario");
                    return Unauthorized(new { success = false, message = "Utente non autenticato" });
                }

                // Create new trip plan
                var tripPlan = new TripPlan
                {
                    Title = model.name,
                    Description = "Itinerario creato da " + currentUser.UserName,
                    UserId = currentUser.Id,
                    StartDate = DateTime.Now,
                    EndDate = DateTime.Now.AddDays(model.stops.Sum(s => s.days)),
                    IsPublic = false,
                    CreatedAt = DateTime.Now,
                    Stops = new List<TripStop>()
                };

                // Add each stop to the trip plan
                for (int i = 0; i < model.stops.Count; i++)
                {
                    var stopModel = model.stops[i];
                    var stop = new TripStop
                    {
                        CityName = stopModel.cityName,
                        Country = stopModel.country ?? "Non specificato",
                        Description = $"Tappa {i + 1}: {stopModel.cityName}",
                        Latitude = stopModel.latitude,
                        Longitude = stopModel.longitude,
                        Days = stopModel.days,
                        Order = i
                    };

                    // Add hotel if selected
                    if (!string.IsNullOrEmpty(stopModel.selectedHotelName))
                    {
                        _logger.LogInformation("Aggiunta hotel {Hotel} alla tappa {City}",
                            stopModel.selectedHotelName, stopModel.cityName);

                        stop.Hotel = new Hotel
                        {
                            Name = stopModel.selectedHotelName,
                            Description = $"Hotel selezionato per il soggiorno a {stopModel.cityName}",
                            PricePerNight = stopModel.selectedHotelPrice,
                            Rating = 4.0 // Valore predefinito
                        };
                    }

                    // Add activities if selected
                    if (stopModel.selectedActivityNames != null && stopModel.selectedActivityNames.Any())
                    {
                        stop.Activities = new List<Activity>();

                        for (int j = 0; j < stopModel.selectedActivityNames.Count; j++)
                        {
                            stop.Activities.Add(new Activity
                            {
                                Name = stopModel.selectedActivityNames[j],
                                Description = $"Attività a {stopModel.cityName}"
                            });
                        }
                    }

                    tripPlan.Stops.Add(stop);
                }

                // Save to database
                _context.TripPlans.Add(tripPlan);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Itinerario salvato con ID {Id}", tripPlan.Id);

                // Generate itinerary content in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var content = await _geminiService.GenerateItineraryContentAsync(tripPlan);
                        tripPlan.GeneratedContent = content;
                        await _context.SaveChangesAsync();
                        _logger.LogInformation("Contenuto itinerario generato per ID {Id}", tripPlan.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Errore generazione contenuto per itinerario ID {Id}", tripPlan.Id);
                    }
                });

                return Json(new { success = true, tripId = tripPlan.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore creazione itinerario: {Message}", ex.Message);
                return StatusCode(500, new { success = false, message = "Errore creazione itinerario: " + ex.Message });
            }
        }

        public async Task<IActionResult> Details(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            var tripPlan = await _context.TripPlans
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Hotel)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Activities)
                .FirstOrDefaultAsync(t => t.Id == id && (t.UserId == currentUser.Id || t.IsPublic));

            if (tripPlan == null)
            {
                return NotFound();
            }

            return View(tripPlan);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var tripPlan = await _context.TripPlans.FirstOrDefaultAsync(t => t.Id == id && t.UserId == currentUser.Id);

            if (tripPlan == null)
            {
                return NotFound();
            }

            _context.TripPlans.Remove(tripPlan);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }

    // ViewModel per il binding dei dati
    public class TripPlanCreateViewModel
    {
        public string name { get; set; }
        public List<StopViewModel> stops { get; set; } = new List<StopViewModel>();
    }

    public class StopViewModel
    {
        public string cityName { get; set; }
        public string country { get; set; }
        public double latitude { get; set; }
        public double longitude { get; set; }
        public int days { get; set; }
        public int order { get; set; }

        public string selectedHotelId { get; set; }
        public string selectedHotelName { get; set; }
        public double selectedHotelPrice { get; set; }

        public List<string> selectedActivityIds { get; set; } = new List<string>();
        public List<string> selectedActivityNames { get; set; } = new List<string>();
    }
}