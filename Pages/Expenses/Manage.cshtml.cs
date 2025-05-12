using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
//using Travel_IA.Models;
using TravelGpt.Data;
using TravelGpt.Models;
using TravelGpt.Services;

namespace TravelGpt.Pages.Expenses
{
    [Authorize]
    public class ManageModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ExpenseService _expenseService;

        public ManageModel(ApplicationDbContext context, ExpenseService expenseService)
        {
            _context = context;
            _expenseService = expenseService;
        }

        public int TripId { get; set; }
        public string TripTitle { get; set; } = string.Empty;
        public List<Expense> Expenses { get; set; } = new List<Expense>();
        public double TotalAmount { get; set; }
        public Dictionary<string, double> ExpensesByCategory { get; set; } = new Dictionary<string, double>();
        public int Participants { get; set; } = 2; // Default to 2 participants
        public double AmountPerPerson { get; set; }

        // Filtri
        [BindProperty(SupportsGet = true)]
        public string? CategoryFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        public DateTime? StartDateFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        public DateTime? EndDateFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool SharedOnlyFilter { get; set; } = false;

        public async Task<IActionResult> OnGetAsync(int tripId)
        {
            TripId = tripId;

            // Recupera il viaggio e verifica che l'utente corrente sia autorizzato
            var trip = await _context.TripPlans
                .FirstOrDefaultAsync(t => t.Id == tripId);

            if (trip == null)
            {
                return NotFound();
            }

            // Verifica l'autorizzazione (solo il proprietario o admin)
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (trip.UserId != userId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            TripTitle = trip.Title;

            // Applica filtri alle spese
            IQueryable<Expense> expensesQuery = _context.Expenses.Where(e => e.TripPlanId == tripId);

            if (!string.IsNullOrEmpty(CategoryFilter))
            {
                expensesQuery = expensesQuery.Where(e => e.Category == CategoryFilter);
            }

            if (StartDateFilter.HasValue)
            {
                expensesQuery = expensesQuery.Where(e => e.Date >= StartDateFilter.Value);
            }

            if (EndDateFilter.HasValue)
            {
                expensesQuery = expensesQuery.Where(e => e.Date <= EndDateFilter.Value);
            }

            if (SharedOnlyFilter)
            {
                expensesQuery = expensesQuery.Where(e => e.IsShared);
            }

            Expenses = await expensesQuery.ToListAsync();

            // Calcola statistiche
            var expenseSummary = await _expenseService.GetExpenseSummaryAsync(tripId);

            TotalAmount = expenseSummary.ContainsKey("TotalAmount") ? expenseSummary["TotalAmount"] : 0;
            AmountPerPerson = TotalAmount / Participants;

            // Estrai categorie
            ExpensesByCategory = Expenses
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int tripId, string description, double amount, string category, DateTime date, bool isShared = false)
        {
            // Verifica che il viaggio esista e che l'utente sia autorizzato
            var trip = await _context.TripPlans.FindAsync(tripId);
            if (trip == null)
            {
                return NotFound();
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (trip.UserId != userId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            // Crea e salva la nuova spesa
            var expense = new Expense
            {
                TripPlanId = tripId,
                Description = description,
                Amount = amount,
                Category = category,
                Date = date,
                IsShared = isShared,
                PaidBy = userId
            };

            await _expenseService.AddExpenseAsync(expense);

            return RedirectToPage(new { tripId });
        }

        public async Task<IActionResult> OnPostEditAsync(int tripId, int expenseId, string description, double amount, string category, DateTime date, bool isShared = false)
        {
            // Verifica che la spesa esista e che l'utente sia autorizzato
            var expense = await _context.Expenses
                .FirstOrDefaultAsync(e => e.Id == expenseId && e.TripPlanId == tripId);

            if (expense == null)
            {
                return NotFound();
            }

            var trip = await _context.TripPlans.FindAsync(tripId);
            if (trip == null)
            {
                return NotFound();
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (trip.UserId != userId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            // Aggiorna i dati della spesa
            expense.Description = description;
            expense.Amount = amount;
            expense.Category = category;
            expense.Date = date;
            expense.IsShared = isShared;

            await _expenseService.UpdateExpenseAsync(expense);

            return RedirectToPage(new { tripId });
        }

        public async Task<IActionResult> OnPostDeleteAsync(int tripId, int expenseId)
        {
            // Verifica che la spesa esista e che l'utente sia autorizzato
            var expense = await _context.Expenses
                .FirstOrDefaultAsync(e => e.Id == expenseId && e.TripPlanId == tripId);

            if (expense == null)
            {
                return NotFound();
            }

            var trip = await _context.TripPlans.FindAsync(tripId);
            if (trip == null)
            {
                return NotFound();
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (trip.UserId != userId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            await _expenseService.DeleteExpenseAsync(expenseId);

            return RedirectToPage(new { tripId });
        }

        // Helper per definire classi CSS per le categorie
        public string GetCategoryBadgeClass(string category)
        {
            return category switch
            {
                "Alloggio" => "bg-primary",
                "Cibo" => "bg-success",
                "Trasporti" => "bg-info",
                "Attività" => "bg-warning",
                "Extra" => "bg-secondary",
                _ => "bg-light"
            };
        }

        public string GetCategoryProgressClass(string category)
        {
            return category switch
            {
                "Alloggio" => "bg-primary",
                "Cibo" => "bg-success",
                "Trasporti" => "bg-info",
                "Attività" => "bg-warning",
                "Extra" => "bg-secondary",
                _ => "bg-light"
            };
        }
    }
}