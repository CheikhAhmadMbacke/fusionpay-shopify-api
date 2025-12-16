using FusionPayProxy.Data;
using FusionPayProxy.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Configuration du port pour Render
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://*:{port}");

// Forçage port 8080 sur Render
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER")))
{
    builder.WebHost.UseUrls($"http://*:8080");
    Console.WriteLine($"🔧 Environnement Render détecté - Forçage port 8080");
}

// Services
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// Swagger
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

    // Configuration de l'authentification par header API Key
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API Key nécessaire pour les endpoints de paiement",
        Type = SecuritySchemeType.ApiKey,
        Name = "moneyfusion-private-key",
        In = ParameterLocation.Header,
        Scheme = "ApiKeyScheme"
    });

    var securityScheme = new OpenApiSecurityScheme
    {
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "ApiKey"
        },
        In = ParameterLocation.Header
    };

    var securityRequirement = new OpenApiSecurityRequirement
    {
        { securityScheme, new List<string>() }
    };

    c.AddSecurityRequirement(securityRequirement);
});

// ⭐⭐⭐ SOLUTION DÉFINITIVE CORS ⭐⭐⭐
// Lecture des origines depuis la configuration
var configAllowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
var defaultOrigins = new[]
{
    "https://fusionpay-shopify-api.onrender.com",
    "https://afrokingvap.com",
    "https://checkout.shopify.com",
    "http://localhost:3000",
    "http://localhost:5000",
    "http://localhost:8080",
    "https://localhost:5001",
    "http://127.0.0.1:3000",
    "http://127.0.0.1:5000"
};

var allowedOrigins = configAllowedOrigins ?? defaultOrigins;

// 🔧 STRATÉGIE CORS INTELLIGENTE
builder.Services.AddCors(options =>
{
    // Option 1: Mode développement - CORS permissif mais SANS credentials
    options.AddPolicy("DevelopmentPolicy",
        policy => policy
            .SetIsOriginAllowed(origin =>
            {
                // Autorise localhost et toutes les origines en développement
                if (builder.Environment.IsDevelopment())
                {
                    return true;
                }

                // En production, vérifie si l'origine est dans la liste
                return allowedOrigins.Any(o =>
                    origin.Equals(o, StringComparison.OrdinalIgnoreCase) ||
                    origin.StartsWith("http://localhost") ||
                    origin.StartsWith("https://localhost") ||
                    origin.StartsWith("http://127.0.0.1"));
            })
            .AllowAnyMethod()
            .AllowAnyHeader()
            // ⚠️ IMPORTANT: Pas de AllowCredentials() avec SetIsOriginAllowed(true)
            .SetPreflightMaxAge(TimeSpan.FromSeconds(86400))); // Cache préflight 24h

    // Option 2: Mode production - CORS strict avec origines spécifiques
    options.AddPolicy("ProductionPolicy",
        policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials() // ✅ OK car WithOrigins spécifiques
            .SetPreflightMaxAge(TimeSpan.FromSeconds(86400)));
});

// Database
var dbPath = builder.Configuration.GetConnectionString("DefaultConnection");
Console.WriteLine($"📁 Chemin base de données: {dbPath}");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(dbPath)
           .LogTo(Console.WriteLine, LogLevel.Information)
           .EnableSensitiveDataLogging(builder.Environment.IsDevelopment()));

// HttpClients
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<FusionPayService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "FusionPayProxy/1.0");
});

// Settings
builder.Services.Configure<FusionPaySettings>(builder.Configuration.GetSection("FusionPay"));
builder.Services.Configure<ShopifySettings>(builder.Configuration.GetSection("Shopify"));

// Services
builder.Services.AddScoped<IFusionPayService, FusionPayService>();
builder.Services.AddScoped<ShopifyService>();

var app = builder.Build();

// ⭐⭐⭐ CONFIGURATION CORS INTELLIGENTE ⭐⭐⭐
// Détermine quelle politique utiliser
if (app.Environment.IsDevelopment())
{
    Console.WriteLine("🔧 Mode: Développement - CORS permissif activé");
    app.UseCors("DevelopmentPolicy");
}
else
{
    Console.WriteLine("🚀 Mode: Production - CORS strict activé");
    app.UseCors("ProductionPolicy");

    // Ajoute un middleware CORS personnalisé pour Swagger
    app.Use(async (context, next) =>
    {
        var origin = context.Request.Headers["Origin"].ToString();
        var path = context.Request.Path.ToString();

        // Autorise Swagger UI depuis notre propre domaine
        if (path.StartsWith("/swagger") || path.StartsWith("/api-docs"))
        {
            context.Response.Headers.Append("Access-Control-Allow-Origin",
                "https://fusionpay-shopify-api.onrender.com");
            context.Response.Headers.Append("Access-Control-Allow-Methods",
                "GET,POST,PUT,DELETE,OPTIONS");
            context.Response.Headers.Append("Access-Control-Allow-Headers",
                "Content-Type,Authorization");
        }

        await next();
    });
}

// Migrations
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
        Console.WriteLine("✅ Base de données migrée avec succès");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Migration échouée: {ex.Message}");
    }
}

// Swagger - TOUJOURS activé
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "FusionPay Proxy API v1");
    c.RoutePrefix = "swagger";
    c.DocumentTitle = "FusionPay Proxy API Documentation";
    c.DisplayRequestDuration();
});

app.UseAuthorization();
app.MapControllers();

// ⭐⭐⭐ ENDPOINTS DE DIAGNOSTIC CORS ⭐⭐⭐

// Health check
app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        message = "FusionPay Proxy API is running!",
        version = "1.0",
        environment = app.Environment.EnvironmentName,
        timestamp = DateTime.UtcNow,
        corsMode = app.Environment.IsDevelopment() ? "Development" : "Production"
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

// Endpoint de diagnostic CORS
app.MapGet("/api/cors-info", (HttpContext context, IConfiguration configuration) =>
{
    var origin = context.Request.Headers["Origin"].ToString();
    var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

    var isOriginAllowed = allowedOrigins.Any(o =>
        origin.Equals(o, StringComparison.OrdinalIgnoreCase) ||
        (app.Environment.IsDevelopment() && origin.StartsWith("http://localhost")));

    return Results.Json(new
    {
        timestamp = DateTime.UtcNow,
        environment = app.Environment.EnvironmentName,
        requestingOrigin = origin,
        isOriginAllowed = isOriginAllowed,
        allowedOrigins = allowedOrigins,
        corsPolicy = app.Environment.IsDevelopment() ? "DevelopmentPolicy" : "ProductionPolicy",
        headers = new
        {
            origin = origin,
            host = context.Request.Host.ToString(),
            userAgent = context.Request.Headers["User-Agent"].ToString()
        }
    });
});

// 🔧 CORRECTION: MapMethods au lieu de MapOptions
// Test CORS avec méthode OPTIONS et POST
app.MapMethods("/api/test-cors", new[] { "OPTIONS" }, () =>
{
    return Results.Ok(new { message = "CORS preflight successful", timestamp = DateTime.UtcNow });
})
.RequireCors(app.Environment.IsDevelopment() ? "DevelopmentPolicy" : "ProductionPolicy");

app.MapPost("/api/test-cors", (HttpContext context) =>
{
    var origin = context.Request.Headers["Origin"].ToString();
    return Results.Json(new
    {
        message = "CORS POST request successful",
        origin = origin,
        timestamp = DateTime.UtcNow,
        environment = app.Environment.EnvironmentName
    });
})
.RequireCors(app.Environment.IsDevelopment() ? "DevelopmentPolicy" : "ProductionPolicy");

// Endpoint de test simple pour l'API
app.MapGet("/api/test", () =>
{
    return Results.Ok(new
    {
        message = "API is working correctly",
        status = "operational",
        timestamp = DateTime.UtcNow,
        features = new[] { "FusionPay Integration", "Shopify Sync", "Webhook Handling", "CORS Configured" }
    });
});

Console.WriteLine($"\n🚀 FusionPay Proxy API démarrée sur .NET 8");
Console.WriteLine($"📊 Base de données: SQLite");
Console.WriteLine($"🌐 Environnement: {app.Environment.EnvironmentName}");
Console.WriteLine($"🔗 CORS Mode: {(app.Environment.IsDevelopment() ? "Développement (permissif)" : "Production (strict)")}");
Console.WriteLine($"🔧 Port: {port}");
Console.WriteLine($"\n✅ Origines CORS autorisées:");
foreach (var origin in allowedOrigins)
{
    Console.WriteLine($"   • {origin}");
}
Console.WriteLine($"\n🔍 Endpoints de diagnostic:");
Console.WriteLine($"   - Health: /health");
Console.WriteLine($"   - CORS Info: /api/cors-info");
Console.WriteLine($"   - Swagger: /swagger");
Console.WriteLine($"   - Test CORS: /api/test-cors (OPTIONS & POST)");
Console.WriteLine($"   - API Test: /api/test");
Console.WriteLine($"\n🎯 Prêt à recevoir des requêtes!");

app.Run();