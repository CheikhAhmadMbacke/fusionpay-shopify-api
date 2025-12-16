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
    });

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FusionPay Proxy API",
        Version = "1.0",
        Description = "API proxy for FusionPay payments"
    });
});

// ⚠️ CORS FLEXIBLE POUR TESTS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy => policy
            .AllowAnyOrigin()
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

// ⚠️ TOUJOURS activer Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "FusionPay Proxy API v1");
    c.RoutePrefix = "swagger";
});

// ⚠️ CORS AVANT TOUT
app.UseCors("AllowAll");

app.UseAuthorization();
app.MapControllers();

// Endpoints
app.MapGet("/", () => "FusionPay Proxy API is running!");
app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });

Console.WriteLine($"🚀 API démarrée sur le port {port}");
Console.WriteLine($"🔗 CORS: Autorise toutes les origines (mode test)");
Console.WriteLine($"🌐 Swagger: https://fusionpay-shopify-api.onrender.com/swagger");

app.Run();