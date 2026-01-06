using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using GamesWorker;

Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // MongoDB
        var mongoUri =
            context.Configuration["MongoDB:ConnectionString"]
            ?? context.Configuration["MONGODB_URI"]
            ?? Environment.GetEnvironmentVariable("MONGODB_URI");

        if (string.IsNullOrWhiteSpace(mongoUri))
            throw new InvalidOperationException("MongoDB connection string not found (MongoDB:ConnectionString no appsettings ou env MONGODB_URI).");

        services.AddSingleton<IMongoClient>(_ =>
        {
            var settings = MongoClientSettings.FromConnectionString(mongoUri);
            settings.ServerApi = new ServerApi(ServerApiVersion.V1);
            return new MongoClient(settings);
        });

        services.AddSingleton(sp =>
        {
            var url = new MongoUrl(mongoUri);
            var dbName = url.DatabaseName ?? "fcg-db";
            return sp.GetRequiredService<IMongoClient>().GetDatabase(dbName);
        });

        // Worker
        services.AddHostedService<GameEventsWorker>();

        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService(serviceName: "games-worker"))
                .WithTracing(t =>
                {
                    t.AddHttpClientInstrumentation();
                    t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                });
        }
    })
    .Build();

Console.WriteLine("[GamesWorker] Iniciando...");
await host.RunAsync();

