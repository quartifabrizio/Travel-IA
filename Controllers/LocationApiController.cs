using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using TravelGpt.Services;

namespace TravelGpt.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    public class LocationDetailsController : ControllerBase
    {
        private readonly GeminiService _geminiService;
        private readonly ILogger<LocationDetailsController> _logger;

        public LocationDetailsController(GeminiService geminiService, ILogger<LocationDetailsController> logger)
        {
            _geminiService = geminiService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Get(string cityName)
        {
            if (string.IsNullOrEmpty(cityName))
            {
                return BadRequest("Il nome della città è obbligatorio.");
            }

            try
            {
                // Aumentiamo il timeout usando un CancellationToken con timeout più lungo
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // 30 secondi invece di 10

                // Implementiamo una Task che termina al timeout
                var timeoutTask = Task.Delay(Timeout.Infinite, cts.Token);

                // Task principale per ottenere i dettagli della località
                var locationTask = Task.Run(async () => await _geminiService.GetLocationDetailsAsync(cityName));

                // Attendiamo il completamento di una delle due task
                var completedTask = await Task.WhenAny(locationTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("Timeout getting location details for {CityName}", cityName);
                    // Non interrompiamo la richiesta, attendiamo ancora un po'
                    var locationDetails = await _geminiService.GetLocationDetailsAsync(cityName);
                    if (locationDetails != null)
                    {
                        return Ok(locationDetails);
                    }
                    return StatusCode(504, "La richiesta ha impiegato troppo tempo per essere elaborata.");
                }

                var result = await locationTask;
                if (result == null)
                {
                    return NotFound($"Nessuna informazione trovata per {cityName}.");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting location details for {CityName}", cityName);
                return StatusCode(500, "Si è verificato un errore durante il recupero delle informazioni.");
            }
        }
    }
}