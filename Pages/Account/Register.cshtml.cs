using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using TravelGpt.Models;

namespace TravelGpt.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<AppUser> _signInManager;
        private readonly UserManager<AppUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<RegisterModel> _logger;

        public RegisterModel(
            UserManager<AppUser> userManager,
            SignInManager<AppUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            ILogger<RegisterModel> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? ReturnUrl { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Il nome è obbligatorio")]
            [Display(Name = "Nome")]
            [StringLength(50, ErrorMessage = "Il {0} deve essere lungo almeno {2} e massimo {1} caratteri.", MinimumLength = 2)]
            public string FirstName { get; set; } = string.Empty;

            [Required(ErrorMessage = "Il cognome è obbligatorio")]
            [Display(Name = "Cognome")]
            [StringLength(50, ErrorMessage = "Il {0} deve essere lungo almeno {2} e massimo {1} caratteri.", MinimumLength = 2)]
            public string LastName { get; set; } = string.Empty;

            [Required(ErrorMessage = "L'Email è obbligatoria")]
            [EmailAddress(ErrorMessage = "Formato email non valido")]
            [Display(Name = "Email")]
            public string Email { get; set; } = string.Empty;

            [Required(ErrorMessage = "La password è obbligatoria")]
            [StringLength(100, ErrorMessage = "La {0} deve essere lunga almeno {2} e massimo {1} caratteri.", MinimumLength = 8)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            [Display(Name = "Conferma password")]
            [Compare("Password", ErrorMessage = "La password e la conferma password non corrispondono.")]
            public string ConfirmPassword { get; set; } = string.Empty;

            [Required(ErrorMessage = "Devi accettare i termini e le condizioni per continuare")]
            [Display(Name = "Accetto i termini e condizioni")]
            public bool AcceptTerms { get; set; }
        }

        public void OnGet(string? returnUrl = null)
        {
            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            if (ModelState.IsValid)
            {
                _logger.LogInformation("Tentativo di registrazione nuovo utente: {Email}", Input.Email);

                // Crea un nuovo utente con le informazioni fornite
                var user = new AppUser
                {
                    UserName = Input.Email,
                    Email = Input.Email,
                    FirstName = Input.FirstName,
                    LastName = Input.LastName,
                    CreatedAt = DateTime.UtcNow
                };

                // Verifica se l'utente esiste già
                var existingUser = await _userManager.FindByEmailAsync(Input.Email);
                if (existingUser != null)
                {
                    ModelState.AddModelError(string.Empty, "Email già registrata. Utilizza un altro indirizzo email.");
                    return Page();
                }

                try
                {
                    // Crea l'utente con la password specificata
                    var result = await _userManager.CreateAsync(user, Input.Password);

                    if (result.Succeeded)
                    {
                        _logger.LogInformation("Utente {Email} creato con successo.", user.Email);

                        // Assegna il ruolo Cliente all'utente
                        // Verifica prima che il ruolo esista
                        if (!await _roleManager.RoleExistsAsync("Cliente"))
                        {
                            _logger.LogWarning("Il ruolo 'Cliente' non esiste. Creazione del ruolo.");
                            await _roleManager.CreateAsync(new IdentityRole("Cliente"));
                        }

                        await _userManager.AddToRoleAsync(user, "Cliente");
                        _logger.LogInformation("Ruolo 'Cliente' assegnato all'utente {Email}.", user.Email);

                        // Accedi l'utente automaticamente
                        await _signInManager.SignInAsync(user, isPersistent: false);
                        _logger.LogInformation("Utente {Email} autenticato con successo.", user.Email);

                        return LocalRedirect(returnUrl);
                    }

                    // Se la creazione dell'utente fallisce, aggiungi gli errori al ModelState
                    foreach (var error in result.Errors)
                    {
                        _logger.LogWarning("Errore durante la registrazione: {Error}", error.Description);
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Errore durante la registrazione dell'utente {Email}", Input.Email);
                    ModelState.AddModelError(string.Empty, "Si è verificato un errore durante la registrazione. Riprova più tardi.");
                }
            }
            else
            {
                // Log degli errori di validazione
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                _logger.LogWarning("Errori di validazione durante la registrazione: {Errors}", string.Join(", ", errors));
            }

            // Se arriviamo qui, qualcosa è andato storto, mostra nuovamente il form
            return Page();
        }
    }
}