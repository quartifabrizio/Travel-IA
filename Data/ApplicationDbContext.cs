using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using TravelGpt.Models;

namespace TravelGpt.Data
{
    public class ApplicationDbContext : IdentityDbContext<AppUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<TripPlan> TripPlans { get; set; }
        public DbSet<TripStop> TripStops { get; set; }
        public DbSet<TravelGpt.Models.Activity> Activities { get; set; }
        public DbSet<Hotel> Hotels { get; set; }
        public DbSet<Expense> Expenses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure relationships
            modelBuilder.Entity<TripPlan>()
                .HasMany(p => p.Stops)
                .WithOne(s => s.TripPlan)
                .HasForeignKey(s => s.TripPlanId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TripStop>()
                .HasMany(s => s.Activities)
                .WithOne(a => a.TripStop)
                .HasForeignKey(a => a.TripStopId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TripPlan>()
                .HasMany(p => p.Expenses)
                .WithOne(e => e.TripPlan)
                .HasForeignKey(e => e.TripPlanId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}