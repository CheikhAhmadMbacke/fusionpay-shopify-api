using FusionPayProxy.Data;
using FusionPayProxy.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// ========== CONFIGURATION DES SERVICES ==========
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✅ AJOUTER LE SUPPORT DES FICHIERS STATIQUES (WWWROOT)
builder.Services.AddStaticFilesConfiguration();

// ✅ CONFIGURATION CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAfroKingVap",
        policy =>
        {
            policy.WithOrigins(
                    "https://afrokingvap.com",
                    "https://checkout.shopify.com",
                    "https://fusionpay-shopify-api.onrender.com",
                    "http://localhost:3000",
                    "http://localhost:5000"
                )
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials()
                .WithExposedHeaders("Location", "X-Payment-Token", "X-Order-Id");
        });
});

// ✅ CONFIGURATION DE LA BASE DE DONNÉES
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

    // Pour Render.com, utiliser le chemin persistant
    if (builder.Environment.IsProduction())
    {
        var renderDataPath = "/opt/render/project/src/data";
        if (Directory.Exists(renderDataPath))
        {
            connectionString = $"Data Source={renderDataPath}/fusionpay.db";
        }
    }

    options.UseSqlite(connectionString);

    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

// ✅ CONFIGURATION HTTP CLIENT
builder.Services.AddHttpClient("FusionPay", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
});

// ✅ ENREGISTREMENT DES SERVICES
builder.Services.AddScoped<IFusionPayService, FusionPayService>();
builder.Services.AddScoped<ShopifyService>();

// ✅ CONFIGURATION DE L'APPLICATION
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("App"));
builder.Services.Configure<FusionPaySettings>(builder.Configuration.GetSection("FusionPay"));
builder.Services.Configure<ShopifySettings>(builder.Configuration.GetSection("Shopify"));

var app = builder.Build();

// ========== CONFIGURATION DU PIPELINE HTTP ==========

// ✅ SERVIR LES FICHIERS STATIQUES (DOIT ÊTRE AVANT UseRouting)
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "wwwroot")),
    RequestPath = "",
    ServeUnknownFileTypes = true, // Pour servir .html
    DefaultContentType = "text/html"
});

// ✅ REDIRECTION HTTPS EN PRODUCTION
if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

// ✅ SWAGGER EN DÉVELOPPEMENT
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "FusionPay API v1");
        options.RoutePrefix = "api-docs";
    });
}

app.UseRouting();

// ✅ CORS (DOIT ÊTRE APRÈS UseRouting et AVANT UseAuthorization)
app.UseCors("AllowAfroKingVap");

app.UseAuthorization();

// ✅ MAPPAGE DES CONTROLLERS
app.MapControllers();

// ✅ ROUTES CUSTOM POUR LES PAGES STATIQUES
app.MapGet("/", () => Results.Redirect("/thank-you.html"));
app.MapGet("/thank-you", () => Results.Redirect("/thank-you.html"));

// ✅ PAGE DE PRÉSENTATION DE L'API
app.MapGet("/about", () =>
{
    var appName = builder.Configuration["App:AppName"] ?? "FusionPay Proxy API";
    var version = builder.Configuration["App:Version"] ?? "1.0.0";

    return Results.Ok(new
    {
        Application = appName,
        Version = version,
        Description = "API d'intégration FusionPay pour AfroKingVap",
        Endpoints = new[]
        {
            "/api/payment/initiate - Initier un paiement",
            "/api/payment/verify/{token} - Vérifier un paiement",
            "/api/webhook/fusionpay - Webhook FusionPay",
            "/thank-you.html - Page de remerciement",
            "/api/payment/health - Health check"
        },
        Documentation = "/api-docs",
        Timestamp = DateTime.UtcNow
    });
});

// ✅ HEALTH CHECK AVANCÉ
app.MapGet("/health", async (AppDbContext dbContext) =>
{
    try
    {
        // Vérifier la base de données
        var canConnect = await dbContext.Database.CanConnectAsync();

        // Vérifier les configurations
        var fusionPayUrl = builder.Configuration["FusionPay:ApiBaseUrl"];
        var yourApiUrl = builder.Configuration["FusionPay:YourApiBaseUrl"];

        return Results.Ok(new
        {
            Status = "Healthy",
            Database = canConnect ? "Connected" : "Disconnected",
            Timestamp = DateTime.UtcNow,
            Services = new
            {
                FusionPay = !string.IsNullOrEmpty(fusionPayUrl) ? "Configured" : "Not Configured",
                ReturnUrl = !string.IsNullOrEmpty(yourApiUrl) ? "Configured" : "Not Configured"
            },
            Environment = app.Environment.EnvironmentName,
            Uptime = Environment.TickCount / 1000 // secondes
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Unhealthy",
            detail: ex.Message,
            statusCode: 503);
    }
});

// ✅ GESTION DES ERREURS
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.MapGet("/error", () => Results.Problem(
        title: "Une erreur est survenue",
        detail: "Veuillez réessayer ultérieurement",
        statusCode: 500));
}

app.Run();

// ✅ EXTENSION METHOD POUR LES FICHIERS STATIQUES
public static class ServiceExtensions
{
    public static IServiceCollection AddStaticFilesConfiguration(this IServiceCollection services)
    {
        // Assure que le dossier wwwroot existe
        var env = services.BuildServiceProvider().GetRequiredService<IWebHostEnvironment>();
        var wwwrootPath = Path.Combine(env.ContentRootPath, "wwwroot");

        if (!Directory.Exists(wwwrootPath))
        {
            Directory.CreateDirectory(wwwrootPath);
        }

        return services;
    }
}

// ✅ CLASSES DE CONFIGURATION
public class AppSettings
{
    public string ThankYouPageBaseUrl { get; set; } = string.Empty;
    public string ShopUrl { get; set; } = string.Empty;
    public string SupportPhone { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

public class FusionPaySettings
{
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string YourApiBaseUrl { get; set; } = string.Empty;
}

public class ShopifySettings
{
    public string ShopDomain { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
}