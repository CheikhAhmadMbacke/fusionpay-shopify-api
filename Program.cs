using FusionPayProxy.Data;
using FusionPayProxy.Services;
using Microsoft.EntityFrameworkCore;

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
    });

// Swagger SIMPLIFIÉ - PAS D'AUTH API KEY
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "FusionPay Proxy API",
        Version = "1.0",
        Description = "API proxy for FusionPay payments integration with Shopify"
    });

    // ✅ PAS DE CONFIGURATION API KEY - FusionPay n'en a pas besoin
});

// 🔧 CORS SIMPLE ET EFFICACE
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? new[]
    {
        "https://fusionpay-shopify-api.onrender.com",
        "https://afrokingvap.com",
        "https://checkout.shopify.com",
        "http://localhost:3000",
        "http://localhost:5000",
        "http://localhost:8080"
    };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowConfiguredOrigins",
        policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

// Database
var dbPath = builder.Configuration.GetConnectionString("DefaultConnection");
Console.WriteLine($"📁 Chemin base de données: {dbPath}");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(dbPath));

// HttpClients
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<FusionPayService>();

// Settings
builder.Services.Configure<FusionPaySettings>(builder.Configuration.GetSection("FusionPay"));
builder.Services.Configure<ShopifySettings>(builder.Configuration.GetSection("Shopify"));

// Services
builder.Services.AddScoped<IFusionPayService, FusionPayService>();
builder.Services.AddScoped<ShopifyService>();

var app = builder.Build();

// 🔧 CORS - TOUJOURS ACTIF
app.UseCors("AllowConfiguredOrigins");

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
});

app.UseAuthorization();
app.MapControllers();

// Endpoints simples
app.MapGet("/", () => "FusionPay Proxy API is running!");
app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });

// Endpoint de test CORS
app.MapMethods("/api/test-cors", new[] { "OPTIONS" }, () =>
{
    return Results.Ok(new { message = "CORS preflight successful", timestamp = DateTime.UtcNow });
})
.RequireCors("AllowConfiguredOrigins");

app.MapPost("/api/test-cors", (HttpContext context) =>
{
    var origin = context.Request.Headers["Origin"].ToString();
    return Results.Json(new
    {
        message = "CORS POST request successful",
        origin = origin,
        timestamp = DateTime.UtcNow
    });
})
.RequireCors("AllowConfiguredOrigins");

// Test endpoint
app.MapGet("/api/test", () =>
{
    return Results.Ok(new
    {
        message = "API is working correctly",
        status = "operational",
        timestamp = DateTime.UtcNow
    });
});

Console.WriteLine($"\n🚀 FusionPay Proxy API démarrée");
Console.WriteLine($"📊 Base de données: SQLite");
Console.WriteLine($"🌐 Environnement: {app.Environment.EnvironmentName}");
Console.WriteLine($"🔗 CORS: Mode production avec origines spécifiques");
Console.WriteLine($"🔧 Port: {port}");
Console.WriteLine($"\n✅ Origines CORS autorisées:");
foreach (var origin in allowedOrigins)
{
    Console.WriteLine($"   • {origin}");
}
Console.WriteLine($"\n🎯 Prêt à recevoir des requêtes!");

app.Run();