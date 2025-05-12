using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelGpt.Data;
using TravelGpt.Models;

namespace TravelGpt.Pages.Admin
{
    [Authorize(Roles = "Admin")]
    public class DashboardModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<AppUser> _userManager;

        public DashboardModel(ApplicationDbContext context, UserManager<AppUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public int TotalUsers { get; set; }
        public int NewUsersThisMonth { get; set; }
        public int TotalTrips { get; set; }
        public int NewTripsThisMonth { get; set; }
        public int TotalDestinations { get; set; }
        public string TopDestination { get; set; }
        public int AdminCount { get; set; }
        public int ClientCount { get; set; }
        public List<TripWithUser> RecentTrips { get; set; } = new();

        public class TripWithUser
        {
            public string Title { get; set; }
            public DateTime CreatedAt { get; set; }
            public AppUser User { get; set; }
            public List<TripStop> Stops { get; set; } = new();
        }

        public async Task OnGetAsync()
        {
            var startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

            // Statistiche utenti
            TotalUsers = await _context.Users.CountAsync();
            NewUsersThisMonth = await _context.Users
                .Where(u => u.CreatedAt >= startOfMonth)
                .CountAsync();

            // Statistiche viaggi
            TotalTrips = await _context.TripPlans.CountAsync();
            NewTripsThisMonth = await _context.TripPlans
                .Where(t => t.CreatedAt >= startOfMonth)
                .CountAsync();

            // Statistiche destinazioni
            var stops = await _context.TripStops.ToListAsync();
            TotalDestinations = stops.Select(s => s.CityName).Distinct().Count();

            // Destinazione più popolare
            TopDestination = stops
                .GroupBy(s => s.CityName)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "N/A";

            // Statistiche ruoli
            AdminCount = (await _userManager.GetUsersInRoleAsync("Admin")).Count;
            ClientCount = (await _userManager.GetUsersInRoleAsync("Cliente")).Count;

            // Viaggi recenti con utenti
            RecentTrips = await _context.TripPlans
                .Include(t => t.Stops)
                .OrderByDescending(t => t.CreatedAt)
                .Take(10)
                .Select(t => new TripWithUser
                {
                    Title = t.Title,
                    CreatedAt = t.CreatedAt,
                    User = _context.Users.FirstOrDefault(u => u.Id == t.UserId),
                    Stops = t.Stops.ToList()
                })
                .ToListAsync();
        }
    }
}