using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using TravelGpt.Models;

namespace TravelGpt.Data
{
    public class DbInitializer
    {
        public static async Task InitializeAsync(ApplicationDbContext context, UserManager<AppUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            // Ensure database is created and migrations applied
            context.Database.Migrate();

            // Check for roles and create if they don't exist
            string[] roles = new[] { "Admin", "Cliente" };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            // Check if the old "User" role exists, and if so, migrate users to "Cliente"
            if (await roleManager.RoleExistsAsync("User"))
            {
                var usersInUserRole = await userManager.GetUsersInRoleAsync("User");
                foreach (var user in usersInUserRole)
                {
                    await userManager.RemoveFromRoleAsync(user, "User");
                    await userManager.AddToRoleAsync(user, "Cliente");
                }

                // Consider deleting the old role if no longer needed
                // await roleManager.DeleteAsync(await roleManager.FindByNameAsync("User"));
            }

            // Create admin user if it doesn't exist
            var adminEmail = "admin@travelgpt.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);

            if (adminUser == null)
            {
                adminUser = new AppUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true,
                    FirstName = "Admin",
                    LastName = "User",
                    ProfilePictureUrl = "/images/default-avatar.png"
                };

                await userManager.CreateAsync(adminUser, "Admin@123");
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }

            // Seed some example data
            if (!context.TripPlans.Any())
            {
                // Sample trip for demo purposes
                var sampleTrip = new TripPlan
                {
                    Title = "Tour della Toscana",
                    Description = "Visita delle principali città d'arte della Toscana",
                    UserId = adminUser.Id,
                    CreatedAt = DateTime.Now,
                    StartDate = DateTime.Now.AddDays(30),
                    EndDate = DateTime.Now.AddDays(37),
                    GeneratedContent = "Giorno 1: Firenze - Arrivo e check-in in hotel\nGiorno 2: Firenze - Visita agli Uffizi e Ponte Vecchio\nGiorno 3: Siena - Visita del centro storico\nGiorno 4: San Gimignano - Tour delle torri medievali\nGiorno 5: Pisa - Visita alla Torre pendente\nGiorno 6: Lucca - Passeggiata sulle mura\nGiorno 7: Firenze - Partenza",
                    Stops = new List<TripStop>
                    {
                        new TripStop
                        {
                            CityName = "Firenze",
                            Country = "Italia",
                            Description = "Culla del rinascimento",
                            Latitude = 43.7696,
                            Longitude = 11.2558,
                            Days = 3,
                            Hotel = new Hotel
                            {
                                Name = "Hotel Firenze Centro",
                                PricePerNight = 120,
                                Rating = 4.5,
                                Description = "Hotel nel centro storico"
                            },
                            Activities = new List<TravelGpt.Models.Activity>
                            {
                                new TravelGpt.Models.Activity
                                {
                                    Name = "Galleria degli Uffizi",
                                    Description = "Visita al famoso museo",
                                    Price = 20
                                },
                                new TravelGpt.Models.Activity
                                {
                                    Name = "Ponte Vecchio",
                                    Description = "Passeggiata sul ponte medievale",
                                    Price = 0
                                }
                            }
                        },
                        new TripStop
                        {
                            CityName = "Siena",
                            Country = "Italia",
                            Description = "Famosa per il Palio",
                            Latitude = 43.3188,
                            Longitude = 11.3306,
                            Days = 2,
                            Hotel = new Hotel
                            {
                                Name = "Albergo Siena",
                                PricePerNight = 95,
                                Rating = 4.2,
                                Description = "Vista sulla Piazza del Campo"
                            },
                            Activities = new List<TravelGpt.Models.Activity>
                            {
                                new TravelGpt.Models.Activity
                                {
                                    Name = "Piazza del Campo",
                                    Description = "Visita alla famosa piazza",
                                    Price = 0
                                }
                            }
                        }
                    },
                    Expenses = new List<Expense>
                    {
                        new Expense
                        {
                            Description = "Hotel Firenze",
                            Amount = 360, // 3 notti a 120€
                            Category = "Alloggio",
                            Date = DateTime.Now.AddDays(30)
                        },
                        new Expense
                        {
                            Description = "Biglietti Uffizi",
                            Amount = 40, // 2 persone a 20€
                            Category = "Attività",
                            Date = DateTime.Now.AddDays(31)
                        },
                        new Expense
                        {
                            Description = "Hotel Siena",
                            Amount = 190, // 2 notti a 95€
                            Category = "Alloggio",
                            Date = DateTime.Now.AddDays(33)
                        }
                    }
                };

                context.TripPlans.Add(sampleTrip);
                await context.SaveChangesAsync();
            }
        }
    }
}