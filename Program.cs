using FusionPayProxy.Data;
using FusionPayProxy.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
// Configuration du port pour Render
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://*:{port}");

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.WriteIndented = true;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "FusionPay Proxy API",
        Version = "1.0",
        Description = "API proxy for FusionPay payments integration with Shopify"
    });
});

// Configuration CORS (MODIFIÉ)
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? new[] { "https://afrokingvap.com", "https://checkout.shopify.com" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins",
        builder => builder
            .WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

// Configure SQLite Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure HttpClient
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<FusionPayService>();

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
        dbContext.Database.Migrate();
        Console.WriteLine("✅ Database migrated successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Database migration failed: {ex.Message}");
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "FusionPay Proxy API v1");
        c.RoutePrefix = "api-docs";
    });
}
// AVANT app.UseHttpsRedirection();
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseHttpsRedirection();
app.UseCors("ShopifyPolicy");
app.UseAuthorization();
app.MapControllers();

// Health check endpoint
app.MapGet("/", () => "FusionPay Proxy API is running!");
app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });

Console.WriteLine($"🚀 FusionPay Proxy API starting on .NET 8...");
Console.WriteLine($"📊 Database: SQLite");
Console.WriteLine($"🔗 CORS Origins: {string.Join(", ", allowedOrigins)}");

Console.WriteLine($"🚀 FusionPay Proxy API starting on port {port}");
Console.WriteLine($"🌐 Environment: {app.Environment.EnvironmentName}");

app.Run();