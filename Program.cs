using FusionPayProxy.Data;
using FusionPayProxy.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Configuration du port pour Render
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://*:{port}");

// Ajoute ceci pour forcer le port 8080 sur Render si détecté
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER")))
{
    builder.WebHost.UseUrls($"http://*:8080");
    Console.WriteLine($"🔧 Environnement Render détecté - Forçage port 8080");
}

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// Configuration Swagger complète
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FusionPay Proxy API",
        Version = "1.0",
        Description = "API proxy for FusionPay payments integration with Shopify",
        Contact = new OpenApiContact
        {
            Name = "AfroKingVap Support",
            Email = "support@afrokingvap.com",
            Url = new Uri("https://afrokingvap.com")
        }
    });

    // ⚠️ NOTE: Aucune authentification par header n'est requise pour l'API Pay-In FusionPay
    // L'authentification se fait uniquement via l'URL de l'API fournie dans la configuration

    // Inclure les commentaires XML si disponibles
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }

    // Configuration pour les enums
    c.UseAllOfToExtendReferenceSchemas();
});

// Configuration CORS
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? new[] { "https://afrokingvap.com", "https://checkout.shopify.com", "https://fusionpay-shopify-api.onrender.com" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins",
        policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

// Configure SQLite Database
var dbPath = builder.Configuration.GetConnectionString("DefaultConnection");
Console.WriteLine($"📁 Chemin base de données: {dbPath}");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(dbPath)
           .LogTo(Console.WriteLine, LogLevel.Information)
           .EnableSensitiveDataLogging(builder.Environment.IsDevelopment()));

// Configure HttpClients
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<FusionPayService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "FusionPayProxy/1.0");

    // ⚠️ IMPORTANT: Pas de header "moneyfusion-private-key" nécessaire pour l'API Pay-In FusionPay
    // L'authentification se fait uniquement via l'URL de l'API
});

// Configure Settings
builder.Services.Configure<FusionPaySettings>(builder.Configuration.GetSection("FusionPay"));
builder.Services.Configure<ShopifySettings>(builder.Configuration.GetSection("Shopify"));

// Register Services
builder.Services.AddScoped<IFusionPayService, FusionPayService>();
builder.Services.AddScoped<ShopifyService>();

// Détecter si on est en dev avec Ngrok
if (builder.Environment.IsDevelopment())
{
    var ngrokUrl = builder.Configuration["FusionPay:YourApiBaseUrl"];
    if (!string.IsNullOrEmpty(ngrokUrl) && ngrokUrl.Contains("ngrok"))
    {
        Console.WriteLine($"🚀 Ngrok détecté: {ngrokUrl}");
        Console.WriteLine("📝 Webhooks seront envoyés à cette URL");
    }
}

var app = builder.Build();

// Apply database migrations
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
        if (pendingMigrations.Any())
        {
            Console.WriteLine($"📋 Migrations en attente: {string.Join(", ", pendingMigrations)}");
            await dbContext.Database.MigrateAsync();
            Console.WriteLine("✅ Base de données migrée avec succès");
        }
        else
        {
            Console.WriteLine("✅ Base de données à jour");
        }

        // Vérifier que la base est accessible
        var canConnect = await dbContext.Database.CanConnectAsync();
        if (canConnect)
        {
            Console.WriteLine("✅ Connexion à la base de données établie");
        }
        else
        {
            Console.WriteLine("❌ Impossible de se connecter à la base de données");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Échec de la migration de la base de données: {ex.Message}");
        if (ex.InnerException != null)
        {
            Console.WriteLine($"   Détail: {ex.InnerException.Message}");
        }
    }
}

// Configure the HTTP request pipeline
// ACTIVE SWAGGER MÊME EN PRODUCTION (mais avec protection)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "FusionPay Proxy API v1");
    c.RoutePrefix = "swagger"; // Accès via /swagger
    c.DocumentTitle = "FusionPay Proxy API Documentation";

    // Protection basique pour la production
    if (!app.Environment.IsDevelopment())
    {
        c.DisplayRequestDuration();
    }
});

// Redirection HTTPS seulement si on a un certificat valide
if (!app.Environment.IsDevelopment())
{
    // Sur Render, la redirection HTTPS est gérée automatiquement
    // Donc on peut désactiver UseHttpsRedirection si cela cause des problèmes
    // app.UseHttpsRedirection();
}

// Configuration CORS
app.UseCors("AllowSpecificOrigins");

app.UseAuthorization();
app.MapControllers();

// Health check endpoint amélioré
app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        message = "FusionPay Proxy API is running!",
        version = "1.0",
        environment = app.Environment.EnvironmentName,
        timestamp = DateTime.UtcNow,
        endpoints = new
        {
            health = "/health",
            swagger = "/swagger",
            paymentInitiate = "/api/payment/initiate",
            webhook = "/api/webhook/fusionpay"
        }
    });
});

app.MapGet("/health", () =>
{
    return Results.Json(new
    {
        status = "healthy",
        timestamp = DateTime.UtcNow,
        service = "FusionPay Proxy",
        version = "1.0.0"
    });
});

// Endpoint de diagnostic
app.MapGet("/api/diagnostic", async (IConfiguration configuration, IOptions<ShopifySettings> shopifySettings) =>
{
    var shopifyToken = shopifySettings.Value.AccessToken;
    var config = new
    {
        Environment = configuration["ASPNETCORE_ENVIRONMENT"],
        Port = port,
        ShopifyDomain = shopifySettings.Value.ShopDomain,
        ShopifyTokenConfigured = !string.IsNullOrEmpty(shopifyToken),
        ShopifyTokenLength = shopifyToken?.Length ?? 0,
        FusionPayUrl = configuration["FusionPay:ApiUrl"],
        DatabasePath = configuration.GetConnectionString("DefaultConnection"),
        AllowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>(),
        RenderEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER"))
    };

    return Results.Json(config);
});

// Endpoint pour tester Shopify
app.MapPost("/api/shopify/test-connection", async (ShopifyService shopifyService) =>
{
    try
    {
        var result = await shopifyService.TestConnectionAsync();
        return Results.Json(new
        {
            success = result,
            message = result ? "Connexion Shopify réussie" : "Connexion Shopify échouée",
            timestamp = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            message = $"Erreur: {ex.Message}",
            timestamp = DateTime.UtcNow
        }, statusCode: 500);
    }
});

Console.WriteLine($"\n🚀 FusionPay Proxy API démarre sur .NET 8...");
Console.WriteLine($"📊 Base de données: SQLite");
Console.WriteLine($"🔗 CORS Origins: {string.Join(", ", allowedOrigins)}");
Console.WriteLine($"🌐 Environnement: {app.Environment.EnvironmentName}");
Console.WriteLine($"🔧 Port: {port}");
Console.WriteLine($"📁 Chemin DB: {dbPath}");
Console.WriteLine($"⚠️  IMPORTANT: API Pay-In FusionPay - Aucune ApiKey requise");
Console.WriteLine($"   L'authentification se fait uniquement via l'URL de l'API");
Console.WriteLine($"\n✅ Prêt! Accédez aux endpoints:");
Console.WriteLine($"   - API: https://fusionpay-shopify-api.onrender.com");
Console.WriteLine($"   - Swagger: https://fusionpay-shopify-api.onrender.com/swagger");
Console.WriteLine($"   - Health: https://fusionpay-shopify-api.onrender.com/health");
Console.WriteLine($"   - Diagnostic: https://fusionpay-shopify-api.onrender.com/api/diagnostic");

app.Run();