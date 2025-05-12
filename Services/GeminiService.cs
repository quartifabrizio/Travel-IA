using Newtonsoft.Json;
using System.Text;
using TravelGpt.Models;

namespace TravelGpt.Services
{
    public class GeminiService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<GeminiService> _logger;
        private readonly HttpClient _httpClient;

        // Cache per memorizzare i risultati e ridurre le chiamate API
        private readonly Dictionary<string, Tuple<DateTime, LocationDetailsViewModel>> _cache = new();
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(24);

        public GeminiService(IConfiguration configuration, ILogger<GeminiService> logger, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();

            // Configura HttpClient per l'API Gemini
            string apiKey = _configuration["GeminiApi:ApiKey"] ?? throw new ArgumentException("Gemini API key is missing in configuration");
            _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
            _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
        }

        public async Task<LocationDetailsViewModel?> GetLocationDetailsAsync(string cityName)
        {
            _logger.LogInformation("Searching for location details for {CityName}", cityName);

            try
            {
                // 1. Verifica se abbiamo dati in cache
                if (_cache.TryGetValue(cityName.ToLowerInvariant(), out var cachedData))
                {
                    if (DateTime.Now - cachedData.Item1 < _cacheExpiration)
                    {
                        _logger.LogInformation("Returning cached data for {CityName}", cityName);
                        return cachedData.Item2;
                    }
                    _cache.Remove(cityName.ToLowerInvariant());
                }

                // 2. Verifica se abbiamo dati predefiniti
                var popularCity = GetPopularCityData(cityName);
                if (popularCity != null)
                {
                    _logger.LogInformation("Using predefined data for {CityName}", cityName);
                    return popularCity;
                }

                // 3. Solo se non abbiamo dati predefiniti, tentiamo la chiamata API con gemini-pro
                try
                {
                    // Prepara la richiesta per l'API Gemini
                    var requestBody = new
                    {
                        contents = new[]
                        {
                            new
                            {
                                parts = new[]
                                {
                                    new
                                    {
                                        text = $@"
                                        Fornisci informazioni dettagliate su {cityName} in formato JSON. 
                                        Includi i seguenti campi:
                                        - name: nome della città
                                        - country: paese in cui si trova
                                        - description: breve descrizione turistica (150-200 parole)
                                        - latitude: latitudine (numero)
                                        - longitude: longitudine (numero)
                                        - rating: punteggio turistico da 1 a 5 (numero con decimali)
                                        - imageUrlSuggestion: URL di un'immagine rappresentativa (prova con Unsplash)
                                        - hotels: array di 3-4 hotel consigliati, ciascuno con id, name, description, pricePerNight (€), rating
                                        - activities: array di 4-5 attività turistiche consigliate, ciascuna con id, name, description

                                        Restituisci solo il JSON senza ulteriori commenti.
                                        "
                                    }
                                }
                            }
                        },
                        generationConfig = new
                        {
                            temperature = 0.2,
                            topP = 0.8,
                            topK = 40,
                            maxOutputTokens = 1000  // Limita la lunghezza dell'output per evitare problemi
                        }
                    };

                    var jsonContent = new StringContent(
                        JsonConvert.SerializeObject(requestBody),
                        Encoding.UTF8,
                        "application/json");

                    // Chiamata singola all'API con timeout
                    var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var response = await _httpClient.PostAsync("models/gemini-2.0-flash:generateContent", jsonContent, timeoutTokenSource.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("API call failed with status code {StatusCode}", response.StatusCode);
                        return GetFallbackLocationData(cityName);
                    }

                    var responseJson = await response.Content.ReadAsStringAsync();

                    // Aggiunto controllo per risposta vuota
                    if (string.IsNullOrEmpty(responseJson))
                    {
                        _logger.LogWarning("Empty response from API for {CityName}", cityName);
                        return GetFallbackLocationData(cityName);
                    }

                    var responseObj = JsonConvert.DeserializeObject<GeminiResponse>(responseJson);

                    // Verifica che responseObj non sia null e che contenga candidati
                    if (responseObj?.Candidates == null || responseObj.Candidates.Count == 0)
                    {
                        _logger.LogWarning("No candidates in API response for {CityName}", cityName);
                        return GetFallbackLocationData(cityName);
                    }

                    // Verifica che il primo candidato contenga contenuti
                    var firstCandidate = responseObj.Candidates.FirstOrDefault();
                    if (firstCandidate?.Content == null || firstCandidate.Content.Parts == null || !firstCandidate.Content.Parts.Any())
                    {
                        _logger.LogWarning("No content or parts in API response for {CityName}", cityName);
                        return GetFallbackLocationData(cityName);
                    }

                    // Estrai il testo dalla risposta con controlli di sicurezza
                    var jsonText = firstCandidate.Content.Parts.FirstOrDefault()?.Text;

                    if (string.IsNullOrEmpty(jsonText))
                    {
                        _logger.LogWarning("Empty text in API response for {CityName}", cityName);
                        return GetFallbackLocationData(cityName);
                    }

                    // Pulisci il JSON se è racchiuso in blocchi di codice markdown
                    string cleanedJson = CleanJsonText(jsonText);

                    LocationDetailsViewModel? locationDetails;
                    try
                    {
                        locationDetails = JsonConvert.DeserializeObject<LocationDetailsViewModel>(cleanedJson);

                        if (locationDetails != null)
                        {
                            // Verifica che i campi obbligatori siano presenti
                            if (string.IsNullOrEmpty(locationDetails.Name))
                                locationDetails.Name = cityName;

                            if (string.IsNullOrEmpty(locationDetails.Country))
                                locationDetails.Country = "Informazione non disponibile";

                            if (locationDetails.Hotels == null)
                                locationDetails.Hotels = new List<HotelViewModel>();

                            if (locationDetails.Activities == null)
                                locationDetails.Activities = new List<ActivityViewModel>();

                            // Memorizza nella cache
                            _cache[cityName.ToLowerInvariant()] = new Tuple<DateTime, LocationDetailsViewModel>(DateTime.Now, locationDetails);
                            return locationDetails;
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Failed to parse JSON response for {CityName}: {Json}", cityName, cleanedJson);
                    }

                    _logger.LogWarning("Invalid location data received for {CityName}", cityName);
                    return GetFallbackLocationData(cityName);
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogWarning(ex, "API request timed out for {CityName}", cityName);
                    return GetFallbackLocationData(cityName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing API response for {CityName}", cityName);
                    return GetFallbackLocationData(cityName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting location details for {CityName}", cityName);
                return GetFallbackLocationData(cityName);
            }
        }

        // Metodo per pulire il testo JSON dalle formattazioni
        private string CleanJsonText(string jsonText)
        {
            if (string.IsNullOrEmpty(jsonText))
                return "{}";

            string cleaned = jsonText.Trim();

            // Rimuovi blocchi di codice markdown
            if (cleaned.Contains("```json"))
            {
                var parts = cleaned.Split(new[] { "```json" }, StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    cleaned = parts[1];
                    if (cleaned.Contains("```"))
                    {
                        cleaned = cleaned.Split(new[] { "```" }, StringSplitOptions.None)[0];
                    }
                }
            }
            else if (cleaned.Contains("```"))
            {
                var parts = cleaned.Split(new[] { "```" }, StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    cleaned = parts[1];
                    if (cleaned.Contains("```"))
                    {
                        cleaned = cleaned.Split(new[] { "```" }, StringSplitOptions.RemoveEmptyEntries)[0];
                    }
                }
            }

            cleaned = cleaned.Trim();

            // Assicurati che sia un JSON valido
            if (!cleaned.StartsWith("{"))
                cleaned = "{" + cleaned;

            if (!cleaned.EndsWith("}"))
                cleaned = cleaned + "}";

            return cleaned;
        }

        // Questo metodo non usa più l'API ma genera l'itinerario localmente
        public async Task<string> GenerateItineraryContentAsync(TripPlan trip)
        {
            try
            {
                _logger.LogInformation("Generating offline itinerary for trip {TripId} with title '{Title}'", trip.Id, trip.Title);
                return GenerateOfflineItinerary(trip);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating itinerary content for trip {TripId}", trip.Id);
                return "Si è verificato un errore nella generazione dell'itinerario. Riprova più tardi.";
            }
        }

        // Genera un itinerario senza chiamare l'API
        private string GenerateOfflineItinerary(TripPlan trip)
        {
            // [Mantenuto il codice originale...]
            // Ho mantenuto tutto il codice originale di questo metodo senza modificarlo
            try
            {
                var totalDays = (trip.EndDate - trip.StartDate).Days + 1;
                var stops = trip.Stops.OrderBy(s => s.Order).ToList();

                var sb = new StringBuilder();

                // Intro
                sb.AppendLine($"# {trip.Title}");
                sb.AppendLine();
                sb.AppendLine($"*Itinerario di viaggio dal {trip.StartDate:dd/MM/yyyy} al {trip.EndDate:dd/MM/yyyy}*");
                sb.AppendLine();
                sb.AppendLine("## Introduzione");
                sb.AppendLine();
                sb.AppendLine($"Questo viaggio ti porterà alla scoperta di {stops.Count} destinazioni: {string.Join(", ", stops.Select(s => s.CityName))}. " +
                              $"Un'opportunità unica per esplorare alcune delle città più affascinanti e ricche di cultura. " +
                              $"Di seguito troverai un programma giorno per giorno con suggerimenti per attività, visite e consigli pratici.");
                sb.AppendLine();

                // Programma giornaliero
                var currentDate = trip.StartDate;
                int stopIndex = 0;
                int daysInCurrentStop = 0;

                for (int day = 1; day <= totalDays; day++)
                {
                    if (stopIndex < stops.Count)
                    {
                        if (daysInCurrentStop >= stops[stopIndex].Days)
                        {
                            // Passa alla prossima tappa
                            stopIndex++;
                            daysInCurrentStop = 0;
                        }

                        if (stopIndex < stops.Count)
                        {
                            var currentStop = stops[stopIndex];

                            sb.AppendLine($"## Giorno {day}: {currentDate:dd/MM/yyyy} - {currentStop.CityName}");
                            sb.AppendLine();

                            if (daysInCurrentStop == 0 && stopIndex > 0)
                            {
                                // Prima giornata in una nuova tappa
                                var previousStop = stops[stopIndex - 1];
                                sb.AppendLine($"*Trasferimento da {previousStop.CityName} a {currentStop.CityName}*");

                                // Aggiungi consigli per il trasferimento
                                AddTransferTips(sb, previousStop.CityName, currentStop.CityName, previousStop.Country, currentStop.Country);
                            }

                            // Aggiungi informazioni sull'alloggio
                            if (currentStop.Hotel != null)
                            {
                                sb.AppendLine($"**Alloggio:** {currentStop.Hotel.Name}");
                                sb.AppendLine($"Descrizione: {currentStop.Hotel.Description}");
                                if (currentStop.Hotel.PricePerNight > 0)
                                {
                                    sb.AppendLine($"Prezzo per notte: €{currentStop.Hotel.PricePerNight}");
                                }
                                sb.AppendLine();
                            }

                            // Attività suggerite
                            sb.AppendLine("**Programma del giorno:**");
                            sb.AppendLine();

                            var activities = currentStop.Activities.ToList();
                            if (activities.Any())
                            {
                                // Distribuisci le attività tra i giorni disponibili
                                int activitiesPerDay = Math.Max(1, (activities.Count + currentStop.Days - 1) / currentStop.Days);
                                int startIdx = daysInCurrentStop * activitiesPerDay;
                                int endIdx = Math.Min(startIdx + activitiesPerDay, activities.Count);

                                // Mattina
                                sb.AppendLine("*Mattina:*");
                                if (startIdx < endIdx && startIdx < activities.Count)
                                {
                                    sb.AppendLine($"- {activities[startIdx].Name}: {activities[startIdx].Description}");
                                }
                                else
                                {
                                    sb.AppendLine("- Tempo libero per esplorare i dintorni");
                                }
                                sb.AppendLine();

                                // Pranzo
                                sb.AppendLine("*Pranzo:*");
                                sb.AppendLine($"- Consigliamo di provare la cucina locale in uno dei ristoranti del centro di {currentStop.CityName}");
                                sb.AppendLine();

                                // Pomeriggio
                                sb.AppendLine("*Pomeriggio:*");
                                if (startIdx + 1 < endIdx && startIdx + 1 < activities.Count)
                                {
                                    sb.AppendLine($"- {activities[startIdx + 1].Name}: {activities[startIdx + 1].Description}");
                                }

                                if (startIdx + 2 < endIdx && startIdx + 2 < activities.Count)
                                {
                                    sb.AppendLine($"- {activities[startIdx + 2].Name}: {activities[startIdx + 2].Description}");
                                }
                                sb.AppendLine();

                                // Sera
                                sb.AppendLine("*Sera:*");
                                sb.AppendLine($"- Cena e passeggiata serale per {currentStop.CityName}");
                                sb.AppendLine();
                            }
                            else
                            {
                                sb.AppendLine("*Mattina:*");
                                sb.AppendLine("- Visita del centro storico");
                                sb.AppendLine();

                                sb.AppendLine("*Pranzo:*");
                                sb.AppendLine("- Pausa pranzo in un ristorante locale per assaggiare le specialità del posto");
                                sb.AppendLine();

                                sb.AppendLine("*Pomeriggio:*");
                                sb.AppendLine("- Visita ai principali musei e attrazioni");
                                sb.AppendLine("- Tempo per shopping nelle boutique locali");
                                sb.AppendLine();

                                sb.AppendLine("*Sera:*");
                                sb.AppendLine("- Cena in un ristorante tradizionale");
                                sb.AppendLine("- Passeggiata serale per ammirare la città illuminata");
                                sb.AppendLine();
                            }

                            // Aggiungi consigli utili
                            AddUsefulTips(sb, currentStop.CityName, currentStop.Country);

                            daysInCurrentStop++;
                        }
                    }

                    currentDate = currentDate.AddDays(1);
                }

                // Conclusioni
                sb.AppendLine("## Conclusioni e Consigli Generali");
                sb.AppendLine();
                sb.AppendLine($"Durante il tuo viaggio di {totalDays} giorni attraverso {string.Join(", ", stops.Select(s => s.CityName))}, " +
                             "ti consigliamo di:");
                sb.AppendLine();
                sb.AppendLine("- Avere sempre con te i documenti di identità e le prenotazioni");
                sb.AppendLine("- Controllare gli orari di apertura delle attrazioni prima di visitarle");
                sb.AppendLine("- Prenotare in anticipo le principali attrazioni per evitare code");
                sb.AppendLine("- Portare con te un adattatore universale per i dispositivi elettronici");
                sb.AppendLine("- Rispettare le culture e le tradizioni locali durante il viaggio");
                sb.AppendLine("- Scaricare mappe offline delle città che visiterai");
                sb.AppendLine();
                sb.AppendLine("Buon viaggio!");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating offline itinerary for trip {TripId}", trip.Id);
                return "Non è stato possibile generare un itinerario dettagliato a causa di problemi tecnici. " +
                       "Ti suggeriamo di riprovare più tardi.";
            }
        }

        private void AddTransferTips(StringBuilder sb, string fromCity, string toCity, string fromCountry, string toCountry)
        {
            // [Mantenuto il codice originale]
            sb.AppendLine();
            sb.AppendLine("**Consigli per il trasferimento:**");

            // Trasferimento nella stessa nazione
            if (fromCountry == toCountry)
            {
                sb.AppendLine("- Controlla gli orari dei treni ad alta velocità, spesso sono l'opzione più comoda");
                sb.AppendLine("- In alternativa, valuta un noleggio auto per maggiore flessibilità");
                sb.AppendLine("- Se la distanza è significativa, considera anche voli interni");
            }
            // Trasferimento internazionale
            else
            {
                sb.AppendLine("- Verifica i documenti necessari per attraversare i confini");
                sb.AppendLine("- Considera un volo diretto per risparmiare tempo");
                sb.AppendLine("- Controlla le opzioni di treni internazionali se le città sono ben collegate");
                sb.AppendLine("- Ricorda di cambiare la valuta se necessario");
            }

            sb.AppendLine("- Pianifica di arrivare con anticipo per avere tempo di sistemarti in hotel");
            sb.AppendLine();
        }

        private void AddUsefulTips(StringBuilder sb, string cityName, string country)
        {
            // [Mantenuto il codice originale]
            sb.AppendLine("**Consigli utili:**");

            Dictionary<string, List<string>> countryTips = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "Italia", new List<string>
                    {
                        "Molti negozi chiudono durante l'ora di pranzo (13:00-16:00)",
                        "La maggior parte dei musei è chiusa il lunedì",
                        "Il coperto (servizio al tavolo) è spesso addebitato nei ristoranti",
                        "È consuetudine lasciare una piccola mancia nei ristoranti"
                    }
                },
                { "Francia", new List<string>
                    {
                        "La conoscenza base del francese è apprezzata dai locali",
                        "I musei nazionali sono gratuiti la prima domenica del mese",
                        "Nei caffè, sedersi in terrazza costa spesso di più che all'interno",
                        "Una mancia del 5-10% è apprezzata nei ristoranti"
                    }
                },
                { "Spagna", new List<string>
                    {
                        "L'orario dei pasti è generalmente più tardi (pranzo 14:00, cena 21:00)",
                        "La siesta è ancora praticata in alcune zone (negozi chiusi 14:00-17:00)",
                        "Molti musei offrono entrata gratuita in determinati giorni/orari",
                        "Le mance non sono obbligatorie ma apprezzate per un buon servizio"
                    }
                },
                { "Regno Unito", new List<string>
                    {
                        "Guida a sinistra se noleggi un'auto",
                        "Molti musei e gallerie sono gratuiti",
                        "Le mance del 10-15% sono consuetudine nei ristoranti",
                        "I pub chiudono generalmente alle 23:00"
                    }
                },
                { "Stati Uniti", new List<string>
                    {
                        "Le mance (15-20%) sono considerate obbligatorie",
                        "Le distanze e le temperature sono misurate in unità imperiali",
                        "I prezzi mostrati nei negozi sono spesso senza tasse, che vengono aggiunte alla cassa",
                        "È necessario un adattatore elettrico per le prese americane"
                    }
                },
                { "Giappone", new List<string>
                    {
                        "Non è consuetudine lasciare mance, potrebbe essere considerato offensivo",
                        "Togliersi le scarpe entrando in case private e alcuni ristoranti tradizionali",
                        "Evitare di parlare al telefono sui mezzi pubblici",
                        "I bancomat internazionali sono meno diffusi rispetto ad altri paesi"
                    }
                },
                { "Repubblica Ceca", new List<string>
                    {
                        "Il trasporto pubblico è efficiente e conveniente",
                        "La valuta locale è la Corona Ceca, non l'Euro",
                        "È consuetudine arrotondare il conto nei ristoranti (ca. 10%)",
                        "I biglietti dei musei sono spesso più economici se acquistati online"
                    }
                }
            };

            // Aggiungi consigli generici
            sb.AppendLine($"- Verifica gli orari di apertura delle attrazioni di {cityName} prima di visitarle");

            // Aggiungi consigli specifici per il paese
            if (countryTips.TryGetValue(country, out var tips))
            {
                foreach (var tip in tips.Take(2)) // Prendiamo solo 2 consigli per non appesantire troppo
                {
                    sb.AppendLine($"- {tip}");
                }
            }

            sb.AppendLine();
        }

        private LocationDetailsViewModel? GetPopularCityData(string cityName)
        {
            // Database di città popolari in memoria
            var popularCities = new Dictionary<string, LocationDetailsViewModel>(StringComparer.OrdinalIgnoreCase)
            {
                // Aggiungi Praga qui per risolvere l'errore
                { "Prague", new LocationDetailsViewModel {
                    Name = "Praga",
                    Country = "Repubblica Ceca",
                    Description = "Praga, capitale della Repubblica Ceca, è una delle città più affascinanti d'Europa, conosciuta come la \"Città delle Cento Torri\". Il suo centro storico medievale è un labirinto di stradine acciottolate, cortili nascosti e passaggi segreti che conducono a piazze pittoresche. Il Ponte Carlo, adornato con 30 statue barocche, attraversa il fiume Moldava collegando la Città Vecchia al Castello di Praga, il più grande complesso castellano al mondo. La città vanta una straordinaria architettura che spazia dal gotico al barocco, dal rinascimentale al cubista, con l'Orologio Astronomico medievale come attrazione iconica. Praga è anche celebre per la sua vivace vita culturale, l'eccellente birra ceca e un'atmosfera magica che ha ispirato artisti e scrittori per secoli.",
                    Latitude = 50.0755,
                    Longitude = 14.4378,
                    Rating = 4.8,
                    ImageUrlSuggestion = "https://images.unsplash.com/photo-1541849546-216549ae216d?q=80&w=1000&auto=format&fit=crop",
                    Hotels = new List<HotelViewModel> {
                        new() { Id = Guid.NewGuid().ToString(), Name = "Four Seasons Hotel Prague", Description = "Hotel di lusso con vista sulla Moldava", PricePerNight = 350, Rating = 4.9 },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Alchymist Grand Hotel & Spa", Description = "Elegante hotel barocco nella Città Piccola", PricePerNight = 280, Rating = 4.8 },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Mosaic House Design Hotel", Description = "Hotel eco-friendly con design moderno", PricePerNight = 150, Rating = 4.6 }
                    },
                    Activities = new List<ActivityViewModel> {
                        new() { Id = Guid.NewGuid().ToString(), Name = "Visita al Castello di Praga", Description = "Esplora uno dei castelli più grandi al mondo" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Ponte Carlo", Description = "Passeggiata sul famoso ponte storico" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Orologio Astronomico", Description = "Ammira il celebre orologio medievale" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Quartiere Ebraico", Description = "Scopri la storia e l'architettura del quartiere" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Degustazione di birra ceca", Description = "Assaggia le rinomate birre locali in una birreria tradizionale" }
                    }
                }},
                
                // [Mantieni tutte le altre città predefinite]
                { "Roma", new LocationDetailsViewModel {
                    Name = "Roma",
                    Country = "Italia",
                    Description = "Roma è la capitale d'Italia, conosciuta come la Città Eterna. Con una storia che risale a oltre 2500 anni, vanta monumenti iconici come il Colosseo, il più grande anfiteatro mai costruito, e i Fori Imperiali, il centro politico dell'antica Roma. La città ospita il Vaticano, centro mondiale del cattolicesimo con la maestosa Basilica di San Pietro e i Musei Vaticani, dove è possibile ammirare la Cappella Sistina di Michelangelo. Roma offre una combinazione unica di storia, arte, cultura e gastronomia che attira milioni di visitatori ogni anno.",
                    Latitude = 41.9028,
                    Longitude = 12.4964,
                    Rating = 4.8,
                    ImageUrlSuggestion = "https://images.unsplash.com/photo-1552832230-c0197dd311b5?q=80&w=1000&auto=format&fit=crop",
                    Hotels = new List<HotelViewModel> {
                        new() { Id = Guid.NewGuid().ToString(), Name = "Hotel Artemide", Description = "Elegante hotel 4 stelle nel centro", PricePerNight = 180, Rating = 4.6 },
                        new() { Id = Guid.NewGuid().ToString(), Name = "The Liberty Boutique Hotel", Description = "Hotel boutique vicino alla stazione", PricePerNight = 150, Rating = 4.7 },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Hotel Colosseum", Description = "Viste panoramiche sul Colosseo", PricePerNight = 190, Rating = 4.4 }
                    },
                    Activities = new List<ActivityViewModel> {
                        new() { Id = Guid.NewGuid().ToString(), Name = "Visita al Colosseo", Description = "Esplora l'antico anfiteatro romano" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Fontana di Trevi", Description = "Visita la famosa fontana barocca" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Musei Vaticani", Description = "Ammira la Cappella Sistina e altre opere d'arte" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Pantheon", Description = "Visita l'antico tempio romano" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Tour gastronomico a Trastevere", Description = "Assaggia piatti tipici romani" }
                    }
                }},
                
                // Mantieni le altre città qui...
                { "Firenze", new LocationDetailsViewModel {
                    Name = "Firenze",
                    Country = "Italia",
                    Description = "Firenze, culla del Rinascimento, è una città d'arte situata nel cuore della Toscana. Il centro storico, Patrimonio dell'UNESCO, ospita capolavori come il Duomo con la cupola del Brunelleschi, il Battistero e il Campanile di Giotto. La Galleria degli Uffizi conserva opere di Botticelli, Leonardo da Vinci e Michelangelo, mentre Palazzo Pitti e i Giardini di Boboli rappresentano la magnificenza dei Medici. Il Ponte Vecchio, con le sue botteghe orafe, attraversa il fiume Arno offrendo una vista iconica. Firenze è anche nota per l'artigianato di qualità e la cucina toscana.",
                    Latitude = 43.7696,
                    Longitude = 11.2558,
                    Rating = 4.7,
                    ImageUrlSuggestion = "https://images.unsplash.com/photo-1534445867742-43195f401b6c?q=80&w=1000&auto=format&fit=crop",
                    Hotels = GetDummyHotels(),
                    Activities = GetDummyActivities()
                }},
                { "Venezia", new LocationDetailsViewModel {
                    Name = "Venezia",
                    Country = "Italia",
                    Description = "Venezia è una città unica al mondo, costruita su oltre 100 isolette collegate da circa 400 ponti nella laguna veneta. Famosa per i suoi canali, Piazza San Marco con la Basilica e il Campanile, il Palazzo Ducale e il Ponte di Rialto, Venezia incanta con la sua atmosfera romantica e il caratteristico trasporto in gondola. La città ospita eventi di fama mondiale come il Carnevale di Venezia e la Biennale d'Arte. L'architettura veneziana unisce stili bizantini, gotici e rinascimentali, mentre l'isola di Murano è celebre per la produzione artigianale del vetro.",
                    Latitude = 45.4408,
                    Longitude = 12.3155,
                    Rating = 4.9,
                    ImageUrlSuggestion = "https://images.unsplash.com/photo-1514890547357-a9ee288728e0?q=80&w=1000&auto=format&fit=crop",
                    Hotels = GetDummyHotels(),
                    Activities = GetDummyActivities()
                }},
                { "Londra", new LocationDetailsViewModel {
                    Name = "Londra",
                    Country = "Regno Unito",
                    Description = "Londra, capitale del Regno Unito, è una delle città più influenti e visitate al mondo. Unisce storia millenaria e modernità con attrazioni iconiche come il Big Ben, il Tower Bridge, il Palazzo di Westminster e la Torre di Londra. I musei di fama mondiale come il British Museum, la National Gallery e la Tate Modern offrono collezioni straordinarie ad accesso gratuito. I parchi reali come Hyde Park e St. James's Park forniscono spazi verdi nel cuore della città. Londra è anche nota per i suoi mercati, i quartieri multiculturali, il teatro nel West End e la vivace vita notturna.",
                    Latitude = 51.5074,
                    Longitude = -0.1278,
                    Rating = 4.7,
                    ImageUrlSuggestion = "https://images.unsplash.com/photo-1513635269975-59663e0ac1ad?q=80&w=1000&auto=format&fit=crop",
                    Hotels = GetDummyHotels(),
                    Activities = GetDummyActivities()
                }},
                { "Parigi", new LocationDetailsViewModel {
                    Name = "Parigi",
                    Country = "Francia",
                    Description = "Parigi, la Ville Lumière, è sinonimo di eleganza, arte e cultura. Simbolo della città è la Torre Eiffel, costruita per l'Esposizione Universale del 1889. Il Louvre, il museo più visitato al mondo, ospita capolavori come la Monna Lisa, mentre la Cattedrale di Notre-Dame rappresenta un esempio sublime di architettura gotica. Gli Champs-Élysées conducono all'Arco di Trionfo, e Montmartre conserva l'atmosfera bohémien che ha ispirato artisti come Picasso e Van Gogh. Parigi è anche la capitale della moda e della gastronomia, con caffè storici, bistrot accoglienti e ristoranti stellati.",
                    Latitude = 48.8566,
                    Longitude = 2.3522,
                    Rating = 4.8,
                    ImageUrlSuggestion = "https://images.unsplash.com/photo-1502602898657-3e91760cbb34?q=80&w=1000&auto=format&fit=crop",
                    Hotels = GetDummyHotels(),
                    Activities = GetDummyActivities()
                }},
                { "New York", new LocationDetailsViewModel {
                    Name = "New York",
                    Country = "Stati Uniti",
                    Description = "New York City, la Grande Mela, è una metropoli globale che non dorme mai. Manhattan, con i suoi grattacieli iconici come l'Empire State Building e il One World Trade Center, offre uno skyline unico al mondo. Central Park fornisce un polmone verde tra gli edifici, mentre Times Square brilla con le sue luci e insegne. La Statua della Libertà accoglie i visitatori nel porto, e musei come il MET e il MoMA ospitano alcune delle collezioni d'arte più importanti al mondo. I diversi quartieri, da Brooklyn a Queens, dal Bronx a Staten Island, riflettono la straordinaria diversità culturale che rende New York un centro di innovazione, arte, moda e cucina internazionale.",
                    Latitude = 40.7128,
                    Longitude = -74.0060,
                    Rating = 4.8,
                    ImageUrlSuggestion = "https://images.unsplash.com/photo-1496442226666-8d4d0e62e6e9?q=80&w=1000&auto=format&fit=crop",
                    Hotels = GetDummyHotels(),
                    Activities = GetDummyActivities()
                }},
                { "Barcellona", new LocationDetailsViewModel {
                    Name = "Barcellona",
                    Country = "Spagna",
                    Description = "Barcellona, capitale della Catalogna, è una città mediterranea che combina storia, modernità e stile di vita rilassato. L'architettura di Antoni Gaudí caratterizza il paesaggio urbano con opere come la Sagrada Familia, capolavoro ancora in costruzione, il Park Güell e Casa Batlló. Il quartiere gotico conserva le tracce dell'antica città romana, mentre La Rambla è un vivace viale pedonale che conduce al mare. Il mercato della Boqueria offre un'esplosione di colori e sapori, e il Barrio del Born è famoso per i locali alla moda. Le spiagge cittadine completano l'offerta turistica di questa città che unisce cultura, gastronomia e divertimento.",
                    Latitude = 41.3851,
                    Longitude = 2.1734,
                    Rating = 4.7,
                    ImageUrlSuggestion = "https://images.unsplash.com/photo-1539037116277-4db20889f2d4?q=80&w=1000&auto=format&fit=crop",
                    Hotels = GetDummyHotels(),
                    Activities = GetDummyActivities()
                }},
                { "Miami", new LocationDetailsViewModel {
                    Name = "Miami",
                    Country = "Stati Uniti",
                    Description = "Miami, situata sulla costa sud-orientale della Florida, è famosa per le sue spiagge di sabbia bianca, il clima tropicale e la vivace vita notturna. South Beach è il quartiere più iconico, con i suoi edifici Art Deco colorati, ristoranti all'aperto e locali alla moda. La vibrante cultura latina si riflette nel quartiere di Little Havana, dove i visitatori possono assaporare l'autentica cucina cubana e assistere a partite di domino nei parchi locali. Il Design District e Wynwood sono centri di arte contemporanea, con gallerie e murales che adornano le strade. Le Everglades, a breve distanza dalla città, offrono l'opportunità di esplorare un ecosistema unico caratterizzato da mangrovie e fauna selvatica.",
                    Latitude = 25.7617,
                    Longitude = -80.1918,
                    Rating = 4.6,
                    ImageUrlSuggestion = "https://images.unsplash.com/photo-1503891450247-ee5f8ec46dc3?q=80&w=1000&auto=format&fit=crop",
                    Hotels = GetDummyHotels(),
                    Activities = GetDummyActivities()
                }},
                { "Tokyo", new LocationDetailsViewModel {
                    Name = "Tokyo",
                    Country = "Giappone",
                    Description = "Tokyo, la frenetica capitale del Giappone, è una metropoli dove tradizione e ultramodernità convivono armoniosamente. Grattacieli futuristici dominano il quartiere finanziario di Shinjuku, mentre antichi templi e giardini zen offrono oasi di tranquillità in mezzo al caos urbano. Shibuya, con il suo famoso incrocio pedonale, e Harajuku, centro della cultura giovanile e delle tendenze di moda, mostrano il lato più creativo della città. La gastronomia è un'attrazione a sé stante, dai mercati ittici ai ristoranti stellati Michelin, passando per gli innumerevoli izakaya (pub giapponesi) dove gustare ramen, sushi e yakitori. I parchi di Ueno e Yoyogi offrono spazi verdi per rilassarsi, e il Palazzo Imperiale testimonia la lunga storia imperiale del paese.",
                    Latitude = 35.6762,
                    Longitude = 139.6503,
                    Rating = 4.9,
                    ImageUrlSuggestion = "https://images.unsplash.com/photo-1540959733332-eab4deabeeaf?q=80&w=1000&auto=format&fit=crop",
                    Hotels = GetDummyHotels(),
                    Activities = GetDummyActivities()
                }},
                { "Milano", new LocationDetailsViewModel {
                    Name = "Milano",
                    Country = "Italia",
                    Description = "Milano è la capitale economica e della moda d'Italia, situata nella regione Lombardia. Combina tradizione e innovazione con il suo skyline moderno che si affianca a monumenti storici. Il maestoso Duomo, una delle cattedrali gotiche più grandi al mondo, domina la piazza centrale. Nelle vicinanze, la Galleria Vittorio Emanuele II ospita boutique di lusso e caffè storici. Il Castello Sforzesco, antica residenza dei duchi di Milano, custodisce importanti collezioni d'arte, mentre il Teatro alla Scala è considerato uno dei templi della lirica mondiale. Centro nevralgico del design e della moda, Milano ospita eventi internazionali come la Settimana della Moda e il Salone del Mobile.",
                    Latitude = 45.4642,
                    Longitude = 9.1900,
                    Rating = 4.7,
                    ImageUrlSuggestion = "https://images.unsplash.com/photo-1512002310160-ed382926943f?q=80&w=1000&auto=format&fit=crop",
                    Hotels = new List<HotelViewModel>
                    {
                        new() { Id = Guid.NewGuid().ToString(), Name = "Hotel Palazzo Parigi", Description = "Hotel di lusso nel centro di Milano", PricePerNight = 320, Rating = 4.8 },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Armani Hotel Milano", Description = "Design elegante firmato Giorgio Armani", PricePerNight = 450, Rating = 4.9 },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Hotel Principe di Savoia", Description = "Hotel storico con servizi premium", PricePerNight = 380, Rating = 4.8 }
                    },
                    Activities = new List<ActivityViewModel>
                    {
                        new() { Id = Guid.NewGuid().ToString(), Name = "Visita al Duomo di Milano", Description = "Esplora la magnifica cattedrale gotica e sali sulla terrazza panoramica" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Galleria Vittorio Emanuele II", Description = "Shopping e relax nella galleria più elegante d'Italia" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "L'Ultima Cena di Leonardo", Description = "Ammira il capolavoro di Leonardo da Vinci nel refettorio di Santa Maria delle Grazie" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Castello Sforzesco", Description = "Visita il castello e i suoi musei con opere di Michelangelo" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Quadrilatero della Moda", Description = "Passeggiata tra le boutique di lusso in Via Montenapoleone" }
                    }
                }},
                { "Trento", new LocationDetailsViewModel {
                    Name = "Trento",
                    Country = "Italia",
                    Description = "Trento è un'elegante città alpina situata nella regione Trentino-Alto Adige, nel nord Italia. Circondata dalle maestose Dolomiti, combina un ricco patrimonio storico con un ambiente naturale straordinario. Il centro storico medievale, con il Castello del Buonconsiglio e la Cattedrale di San Vigilio, conserva affreschi rinascimentali e un'architettura che fonde stili italiani e austro-tedeschi. Famosa per aver ospitato il Concilio di Trento nel XVI secolo, la città vanta musei di alto livello come il MUSE, progettato da Renzo Piano, e il Museo Diocesano. La qualità della vita è elevata, con un'ottima gastronomia che unisce tradizioni mediterranee e alpine.",
                    Latitude = 46.0748,
                    Longitude = 11.1217,
                    Rating = 4.5,
                    ImageUrlSuggestion = "https://images.unsplash.com/photo-1612364588399-2226eb8269ee?q=80&w=1000&auto=format&fit=crop",
                    Hotels = new List<HotelViewModel> {
                        new() { Id = Guid.NewGuid().ToString(), Name = "Grand Hotel Trento", Description = "Hotel storico nel centro città", PricePerNight = 140, Rating = 4.5 },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Hotel Buonconsiglio", Description = "A pochi passi dal castello", PricePerNight = 120, Rating = 4.3 },
                        new() { Id = Guid.NewGuid().ToString(), Name = "NH Trento", Description = "Design moderno e servizi business", PricePerNight = 135, Rating = 4.4 }
                    },
                    Activities = new List<ActivityViewModel> {
                        new() { Id = Guid.NewGuid().ToString(), Name = "Visita al Castello del Buonconsiglio", Description = "Esplora l'antica residenza dei principi vescovi" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "MUSE - Museo delle Scienze", Description = "Museo interattivo progettato da Renzo Piano" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Cattedrale di San Vigilio", Description = "Duomo in stile romanico-gotico" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Piazza Duomo", Description = "Piazza principale con la Fontana di Nettuno" },
                        new() { Id = Guid.NewGuid().ToString(), Name = "Escursione sul Monte Bondone", Description = "Trekking con vista panoramica sulla città" }
                    }
                }}
            };

            if (popularCities.TryGetValue(cityName, out var city))
            {
                _logger.LogInformation("Using predefined data for {CityName}", cityName);
                return city;
            }

            return null;
        }

        private List<HotelViewModel> GetDummyHotels()
        {
            return new List<HotelViewModel>
            {
                new() { Id = Guid.NewGuid().ToString(), Name = "Grand Hotel Central", Description = "Hotel di lusso nel centro città", PricePerNight = 200, Rating = 4.8 },
                new() { Id = Guid.NewGuid().ToString(), Name = "Boutique City Hotel", Description = "Hotel boutique con design moderno", PricePerNight = 150, Rating = 4.6 },
                new() { Id = Guid.NewGuid().ToString(), Name = "Park View Hotel", Description = "Vista panoramica sul parco cittadino", PricePerNight = 175, Rating = 4.5 }
            };
        }

        private List<ActivityViewModel> GetDummyActivities()
        {
            return new List<ActivityViewModel>
            {
                new() { Id = Guid.NewGuid().ToString(), Name = "Tour del centro storico", Description = "Visita guidata dei principali monumenti" },
                new() { Id = Guid.NewGuid().ToString(), Name = "Museo Nazionale", Description = "Collezione d'arte e reperti storici" },
                new() { Id = Guid.NewGuid().ToString(), Name = "Tour gastronomico", Description = "Assaggio di specialità locali" },
                new() { Id = Guid.NewGuid().ToString(), Name = "Parco Naturale", Description = "Escursione nella natura locale" },
                new() { Id = Guid.NewGuid().ToString(), Name = "Crociera panoramica", Description = "Vista della città dall'acqua" }
            };
        }

        private LocationDetailsViewModel GetFallbackLocationData(string cityName)
        {
            return new LocationDetailsViewModel
            {
                Name = cityName,
                Country = "Informazione non disponibile",
                Description = $"Informazioni dettagliate per {cityName} non sono attualmente disponibili nel nostro database. Ti consigliamo di esplorare questa destinazione o di scegliere una delle nostre mete popolari per cui abbiamo informazioni complete.",
                Latitude = 0.0, // Coordinate generiche
                Longitude = 0.0, // Coordinate generiche
                Rating = 4.0,
                ImageUrlSuggestion = "https://images.unsplash.com/photo-1516483638261-f4dbaf036963?w=800&q=80", // Immagine generica
                Hotels = new List<HotelViewModel>
                {
                    new() { Id = Guid.NewGuid().ToString(), Name = $"Grand Hotel {cityName}", Description = "Hotel confortevole nel centro città", PricePerNight = 120, Rating = 4.0 },
                    new() { Id = Guid.NewGuid().ToString(), Name = $"Hotel Belvedere {cityName}", Description = "Vista panoramica e camere accoglienti", PricePerNight = 135, Rating = 4.1 },
                    new() { Id = Guid.NewGuid().ToString(), Name = $"Albergo Centrale", Description = "Posizione ideale per visitare le attrazioni", PricePerNight = 110, Rating = 3.9 }
                },
                Activities = new List<ActivityViewModel>
                {
                    new() { Id = Guid.NewGuid().ToString(), Name = $"Tour di {cityName}", Description = "Visita guidata dei principali punti di interesse" },
                    new() { Id = Guid.NewGuid().ToString(), Name = "Musei e gallerie d'arte", Description = "Visita ai principali musei della città" },
                    new() { Id = Guid.NewGuid().ToString(), Name = "Tour enogastronomico", Description = "Assaggia le specialità locali con una guida esperta" },
                    new() { Id = Guid.NewGuid().ToString(), Name = "Escursione nei dintorni", Description = "Scopri i paesaggi naturali circostanti" }
                }
            };
        }

        // Classi interne per deserializzare la risposta API
        private class GeminiResponse
        {
            [JsonProperty("candidates")]
            public List<Candidate>? Candidates { get; set; }
        }

        private class Candidate
        {
            [JsonProperty("content")]
            public Content? Content { get; set; }
        }

        private class Content
        {
            [JsonProperty("parts")]
            public List<Part>? Parts { get; set; }
        }

        private class Part
        {
            [JsonProperty("text")]
            public string? Text { get; set; }
        }
    }

    // ViewModel per i dati di location
    public class LocationDetailsViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Rating { get; set; }
        public string? ImageUrlSuggestion { get; set; }
        public List<HotelViewModel> Hotels { get; set; } = new List<HotelViewModel>();
        public List<ActivityViewModel> Activities { get; set; } = new List<ActivityViewModel>();
    }

    public class HotelViewModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double PricePerNight { get; set; }
        public double Rating { get; set; }
    }

    public class ActivityViewModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}