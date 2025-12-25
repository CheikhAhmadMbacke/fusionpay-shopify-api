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
            Console.WriteLine($"📁 Using Render persistent path: {connectionString}");
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

// ✅ AJOUTER LE LOGGING POUR LE CONTEXTE DE BASE DE DONNÉES
builder.Services.AddLogging();

var app = builder.Build();

// ========== INITIALISATION DE LA BASE DE DONNÉES ==========
// ✅ EXÉCUTER AVANT TOUT LE RESTE
try
{
    using (var scope = app.Services.CreateScope())
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        logger.LogInformation("🚀 Starting database initialization...");

        // Méthode 1: Vérifier la connexion
        var canConnect = dbContext.Database.CanConnect();
        logger.LogInformation(canConnect ? "✅ Database connection successful" : "⚠️ Cannot connect to database");

        // Méthode 2: Vérifier les tables
        var tablesCheckSql = @"
            SELECT name FROM sqlite_master 
            WHERE type='table' AND name IN ('Transactions', 'ShopifyOrders', 'WebhookLogs')";

        try
        {
            var existingTables = new List<string>();
            using (var command = dbContext.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = tablesCheckSql;
                dbContext.Database.OpenConnection();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        existingTables.Add(reader.GetString(0));
                    }
                }
            }

            logger.LogInformation($"📊 Found {existingTables.Count}/3 tables: {string.Join(", ", existingTables)}");

            if (existingTables.Count < 3)
            {
                logger.LogWarning("⚠️ Some tables are missing, ensuring database is created...");
                dbContext.Database.EnsureCreated();
                logger.LogInformation("✅ Database tables created/verified");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Error checking tables, trying to create database...");
            dbContext.Database.EnsureCreated();
            logger.LogInformation("✅ Database created as fallback");
        }

        // Méthode 3: Appliquer les migrations si disponibles
        try
        {
            var pendingMigrations = dbContext.Database.GetPendingMigrations().ToList();
            if (pendingMigrations.Any())
            {
                logger.LogInformation($"🔄 Applying {pendingMigrations.Count} pending migrations...");
                dbContext.Database.Migrate();
                logger.LogInformation("✅ Migrations applied successfully");
            }
        }
        catch (Exception migrationEx)
        {
            logger.LogWarning(migrationEx, "⚠️ Could not apply migrations, using current schema");
        }

        logger.LogInformation("🎯 Database initialization completed");
    }
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "💥 FATAL: Could not initialize database");
    // Ne pas arrêter l'application, elle peut fonctionner en mode dégradé
}

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

        // Vérifier les tables
        var tablesCheckSql = @"
            SELECT COUNT(*) FROM sqlite_master 
            WHERE type='table' AND name IN ('Transactions', 'ShopifyOrders', 'WebhookLogs')";

        var tablesCount = await dbContext.Database.ExecuteSqlRawAsync(tablesCheckSql);

        // Vérifier les configurations
        var fusionPayUrl = builder.Configuration["FusionPay:ApiBaseUrl"];
        var yourApiUrl = builder.Configuration["FusionPay:YourApiBaseUrl"];

        return Results.Ok(new
        {
            Status = "Healthy",
            Database = canConnect ? "Connected" : "Disconnected",
            Tables = $"{tablesCount}/3 tables present",
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

// ✅ ENDPOINT POUR FORCER LA CRÉATION DE LA BASE (DEBUG)
app.MapGet("/admin/init-db", async (AppDbContext dbContext) =>
{
    try
    {
        await dbContext.Database.EnsureCreatedAsync();
        return Results.Ok(new
        {
            success = true,
            message = "Database initialized successfully",
            timestamp = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Database initialization failed",
            detail: ex.Message,
            statusCode: 500);
    }
});

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