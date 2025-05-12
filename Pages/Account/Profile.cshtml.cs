using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using TravelGpt.Data;
using TravelGpt.Models;

namespace TravelGpt.Pages.Account
{
    [Authorize]
    public class ProfileModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public ProfileModel(
            UserManager<AppUser> userManager,
            SignInManager<AppUser> signInManager,
            ApplicationDbContext context,
            IWebHostEnvironment environment)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
            _environment = environment;
        }

        // Proprietà utente
        public string Username { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string ProfilePictureUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TripsCount { get; set; }
        public string Role { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        [BindProperty]
        public InputModel Input { get; set; }

        [BindProperty]
        public PasswordModel PasswordInput { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Il nome è obbligatorio")]
            [Display(Name = "Nome")]
            public string FirstName { get; set; }

            [Required(ErrorMessage = "Il cognome è obbligatorio")]
            [Display(Name = "Cognome")]
            public string LastName { get; set; }

            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; }

            [Phone]
            [Display(Name = "Numero di telefono")]
            public string PhoneNumber { get; set; }
        }

        public class PasswordModel
        {
            [Required(ErrorMessage = "La password attuale è obbligatoria")]
            [DataType(DataType.Password)]
            [Display(Name = "Password attuale")]
            public string OldPassword { get; set; }

            [Required(ErrorMessage = "La nuova password è obbligatoria")]
            [StringLength(100, ErrorMessage = "La {0} deve essere almeno di {2} e massimo di {1} caratteri.", MinimumLength = 8)]
            [DataType(DataType.Password)]
            [Display(Name = "Nuova password")]
            public string NewPassword { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Conferma password")]
            [Compare("NewPassword", ErrorMessage = "La nuova password e la password di conferma non corrispondono.")]
            public string ConfirmPassword { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Non è stato possibile caricare l'utente con ID '{_userManager.GetUserId(User)}'.");
            }

            await LoadUserDataAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostUpdateProfileAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Non è stato possibile caricare l'utente con ID '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                await LoadUserDataAsync(user);
                return Page();
            }

            // Aggiorna il numero di telefono
            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);
            if (Input.PhoneNumber != phoneNumber)
            {
                var setPhoneResult = await _userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
                if (!setPhoneResult.Succeeded)
                {
                    StatusMessage = "Errore durante l'aggiornamento del numero di telefono.";
                    await LoadUserDataAsync(user);
                    return Page();
                }
            }

            // Aggiorna nome e cognome
            user.FirstName = Input.FirstName;
            user.LastName = Input.LastName;

            await _userManager.UpdateAsync(user);
            await _signInManager.RefreshSignInAsync(user);

            StatusMessage = "Il tuo profilo è stato aggiornato con successo.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostChangePasswordAsync()
        {
            // Ottieni l'utente prima di effettuare qualsiasi validazione
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Non è stato possibile caricare l'utente con ID '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                await LoadUserDataAsync(user);
                return Page();
            }

            var changePasswordResult = await _userManager.ChangePasswordAsync(
                user,
                PasswordInput.OldPassword,
                PasswordInput.NewPassword);

            if (!changePasswordResult.Succeeded)
            {
                foreach (var error in changePasswordResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                await LoadUserDataAsync(user);
                return Page();
            }

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "La tua password è stata cambiata con successo.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdatePhotoAsync(IFormFile profilePicture)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Non è stato possibile caricare l'utente con ID '{_userManager.GetUserId(User)}'.");
            }

            if (profilePicture != null && profilePicture.Length > 0)
            {
                // Crea la directory delle immagini profilo se non esiste
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "images", "profiles");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // Genera un nome file univoco
                var fileName = $"{user.Id}_{DateTime.Now.Ticks}{Path.GetExtension(profilePicture.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                // Salva l'immagine sul server
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await profilePicture.CopyToAsync(stream);
                }

                // Aggiorna l'URL dell'immagine profilo
                user.ProfilePictureUrl = $"/images/profiles/{fileName}";
                await _userManager.UpdateAsync(user);

                StatusMessage = "La tua foto profilo è stata aggiornata con successo.";
            }
            else
            {
                StatusMessage = "Errore: Nessuna immagine selezionata.";
            }

            return RedirectToPage();
        }

        private async Task LoadUserDataAsync(AppUser user)
        {
            // Carica i dati dell'utente
            Username = user.UserName;
            FirstName = user.FirstName;
            LastName = user.LastName;
            Email = user.Email;
            ProfilePictureUrl = !string.IsNullOrEmpty(user.ProfilePictureUrl)
                ? user.ProfilePictureUrl
                : "/images/default-avatar.png"; // URL immagine predefinita
            CreatedAt = user.CreatedAt;
            TripsCount = _context.TripPlans.Count(t => t.UserId == user.Id);

            // Ottieni il ruolo dell'utente
            var roles = await _userManager.GetRolesAsync(user);
            Role = roles.FirstOrDefault() ?? "Cliente";

            // Precompila il form di modifica profilo
            Input = new InputModel
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PhoneNumber = await _userManager.GetPhoneNumberAsync(user)
            };

            // Inizializza il form per il cambio password
            PasswordInput = new PasswordModel();
        }
    }
}