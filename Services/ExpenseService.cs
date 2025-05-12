using Microsoft.EntityFrameworkCore;
//using Travel_IA.Models;
using TravelGpt.Data;
using TravelGpt.Models;

namespace TravelGpt.Services
{
    public class ExpenseService
    {
        private readonly ApplicationDbContext _context;

        public ExpenseService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<Expense>> GetTripExpensesAsync(int tripId)
        {
            return await _context.Expenses
                .Where(e => e.TripPlanId == tripId)
                .OrderByDescending(e => e.Date)
                .ToListAsync();
        }

        public async Task AddExpenseAsync(Expense expense)
        {
            _context.Expenses.Add(expense);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateExpenseAsync(Expense expense)
        {
            _context.Entry(expense).State = EntityState.Modified;
            await _context.SaveChangesAsync();
        }

        public async Task DeleteExpenseAsync(int expenseId)
        {
            var expense = await _context.Expenses.FindAsync(expenseId);
            if (expense != null)
            {
                _context.Expenses.Remove(expense);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<Dictionary<string, double>> GetExpenseSummaryAsync(int tripId)
        {
            var expenses = await _context.Expenses
                .Where(e => e.TripPlanId == tripId)
                .ToListAsync();

            var summary = new Dictionary<string, double>();

            // Total amount
            summary["TotalAmount"] = expenses.Sum(e => e.Amount);

            // By category
            var categories = expenses.GroupBy(e => e.Category);
            foreach (var category in categories)
            {
                summary[$"Category_{category.Key}"] = category.Sum(e => e.Amount);
            }

            // By date
            var byDate = expenses.GroupBy(e => e.Date.Date);
            foreach (var dateGroup in byDate)
            {
                summary[$"Date_{dateGroup.Key:yyyy-MM-dd}"] = dateGroup.Sum(e => e.Amount);
            }

            // Per person (if shared expenses are divided)
            // This is simplified - in a real app you'd track participants
            var trip = await _context.TripPlans.FindAsync(tripId);
            if (trip != null)
            {
                int participants = 2; // Default to 2 for this example
                summary["PerPerson"] = summary["TotalAmount"] / participants;
            }

            return summary;
        }
    }
}