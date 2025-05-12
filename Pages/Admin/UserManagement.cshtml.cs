using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelGpt.Data;
using TravelGpt.Models;

namespace TravelGpt.Pages.Admin
{
    [Authorize(Roles = "Admin")]
    public class UserManagementModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly ApplicationDbContext _context;

        public UserManagementModel(UserManager<AppUser> userManager, ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public IList<AppUser> Users { get; set; } = new List<AppUser>();

        public async Task OnGetAsync()
        {
            Users = await _context.Users
                .Include(u => u.TripPlans)
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();
        }

        public async Task<string> GetUserRole(AppUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            return roles.FirstOrDefault() ?? "Cliente";
        }

        public async Task<IActionResult> OnPostChangeRoleAsync(string userId, string role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound();
            }

            // Verifica se stiamo rimuovendo l'ultimo admin
            var currentRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault();
            if (currentRole == "Admin" && role != "Admin")
            {
                var adminCount = (await _userManager.GetUsersInRoleAsync("Admin")).Count;
                if (adminCount <= 1)
                {
                    TempData["ErrorMessage"] = "Impossibile rimuovere l'ultimo amministratore dal sistema.";
                    return RedirectToPage();
                }
            }

            // Rimuovi tutti i ruoli correnti
            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Any())
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
            }

            // Aggiungi il nuovo ruolo
            await _userManager.AddToRoleAsync(user, role);

            TempData["SuccessMessage"] = "Il ruolo dell'utente è stato aggiornato con successo.";
            return RedirectToPage();
        }

        public async Task<int> GetAdminCount()
        {
            return (await _userManager.GetUsersInRoleAsync("Admin")).Count;
        }

        public async Task<IActionResult> OnPostDeleteUserAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound();
            }

            // Verifico se non è l'ultimo admin
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Admin"))
            {
                var adminCount = (await _userManager.GetUsersInRoleAsync("Admin")).Count;
                if (adminCount <= 1)
                {
                    TempData["ErrorMessage"] = "Impossibile eliminare l'ultimo amministratore dal sistema.";
                    return RedirectToPage();
                }
            }

            // Elimina tutti i viaggi associati all'utente
            var trips = await _context.TripPlans
                .Include(t => t.Expenses)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Hotel)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Activities)
                .Where(t => t.UserId == userId)
                .ToListAsync();

            foreach (var trip in trips)
            {
                foreach (var stop in trip.Stops)
                {
                    _context.Activities.RemoveRange(stop.Activities);
                    if (stop.Hotel != null)
                    {
                        _context.Hotels.Remove(stop.Hotel);
                    }
                }

                _context.Expenses.RemoveRange(trip.Expenses);
                _context.TripStops.RemoveRange(trip.Stops);
                _context.TripPlans.Remove(trip);
            }

            // Elimina l'utente
            var result = await _userManager.DeleteAsync(user);

            if (result.Succeeded)
            {
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "L'utente è stato eliminato con successo.";
            }
            else
            {
                TempData["ErrorMessage"] = "Si è verificato un errore durante l'eliminazione dell'utente.";
            }

            return RedirectToPage();
        }
    }
}