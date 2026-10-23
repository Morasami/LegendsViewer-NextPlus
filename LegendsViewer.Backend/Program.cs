
using LegendsViewer.Backend.Settings;
using LegendsViewer.Backend.WorldCache;
using LegendsViewer.Backend.Extensions;
using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Bookmarks;
using LegendsViewer.Backend.Legends.Interfaces;
using LegendsViewer.Backend.Legends.Maps;
using LegendsViewer.Backend.Logging;
using LegendsViewer.Frontend;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging.Console;
using System.Text;
using System.Text.Json.Serialization;

namespace LegendsViewer.Backend;

public class Program
{
    private const string LocalOriginsPolicy = "LocalOrigins";
    public const uint BackendPort = 15421;
    public static readonly string BackendUrl = $"http://localhost:{BackendPort}";

    public static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddCors(o => o.AddPolicy(LocalOriginsPolicy, builder =>
        {
            builder.WithOrigins(LocalBrowserAccess.Origins)
                   .AllowAnyMethod()
                   .WithExposedHeaders("X-NextPlus-Cache", "X-NextPlus-Load-Ms", "X-NextPlus-Warning")
                   .AllowAnyHeader();
        }));

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
            serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
        })
        .UseUrls(BackendUrl);

        builder.Services.AddSingleton<WorldCacheService>();
        builder.Services.AddSingleton<WorldSessionStore>();
        builder.Services.AddScoped<WorldSessionLease>(services => services.GetRequiredService<WorldSessionStore>().Lease());
        builder.Services.AddScoped<IWorld>(services => new WorldView(services.GetRequiredService<WorldSessionLease>().World));
        builder.Services.AddScoped<IWorldMapImageGenerator>(services => services.GetRequiredService<WorldSessionLease>().Map);
        builder.Services.AddSingleton<WorldLoadService>();
        builder.Services.AddSingleton<ApplicationSettingsService>();
        builder.Services.AddSingleton<InstallationLocations>();
        builder.Services.AddSingleton<GameInstallationService>();
        builder.Services.AddSingleton<WorldLibrary.WorldLibraryService>();
        builder.Services.AddSingleton<IBookmarkService, BookmarkService>();
        builder.Services.AddClassicRepositories();

        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                // Serialize Enums as strings globally
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.FormatterName = nameof(SimpleLogFormatter));
        builder.Logging.AddConsoleFormatter<SimpleLogFormatter, ConsoleFormatterOptions>();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.Use(async (context, next) =>
        {
            if (!LocalBrowserAccess.IsAllowed(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next();
        });
        app.UseCors(LocalOriginsPolicy);

        app.UseAuthorization();
        app.MapControllers();

        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        logger.LogInformation(AsciiArt.LegendsViewerLogo);
        logger.LogInformation("If the Legends Viewer frontend does not open automatically, you can access it directly by visiting:");
        logger.LogInformation(WebAppStaticServer.WebAppUrl);

        _ = WebAppStaticServer.RunAsync();

        var openBrowser = Task.Delay(0);
        if (!app.Environment.IsDevelopment())
        {
            openBrowser = WebAppStaticServer.OpenPageInBrowserAsync();
        }

        try
        {
            app.Run();
        }
        catch (IOException exception) when (exception.InnerException is AddressInUseException)
        {
            Console.WriteLine($"Address already in use: {BackendUrl}");
            Console.WriteLine("Skipping backend server.");
            Task.WaitAll(openBrowser);
        }
    }
}
