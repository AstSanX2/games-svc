using Amazon;
using Application.Services;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Search;
using Domain.Interfaces.Services;
using Helpers;
using Infraestructure.Options;
using Infraestructure.Repositories;
using Infraestructure.Search;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Text;
using Middlewares;

var builder = WebApplication.CreateBuilder(args);

Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;

// ------------------------------------------------------
// Kestrel otimizado para rodar em container/Kubernetes
// ------------------------------------------------------
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

// Para funcionar bem atrás de ingress/nginx/ALB
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var env = builder.Environment;
var config = builder.Configuration;

var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(serviceName: "games-api"))
        .WithTracing(t =>
        {
            t.SetSampler(new AlwaysOnSampler());
            t.AddSource("games-api.outbox");
            t.AddAspNetCoreInstrumentation();
            t.AddHttpClientInstrumentation();
            t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        });
}

// ----------------- Funções utilitárias -----------------
static string Require(string? v, string error) =>
    string.IsNullOrWhiteSpace(v) ? throw new InvalidOperationException(error) : v;

static string First(params string?[] vals) =>
    vals.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

// ----------------- MongoDB -----------------
var mongoUri = Require(
    First(
        config["MongoDB:ConnectionString"],
        Environment.GetEnvironmentVariable("MONGODB_URI"),
        Environment.GetEnvironmentVariable("MongoDB__ConnectionString")
    ),
    "MongoDB URI não configurada (MongoDB:ConnectionString no appsettings ou env MONGODB_URI)."
);

builder.Services.AddSingleton<IMongoClient>(_ =>
{
    var settings = MongoClientSettings.FromConnectionString(mongoUri);
    settings.ServerApi = new ServerApi(ServerApiVersion.V1);
    return new MongoClient(settings);
});

builder.Services.AddSingleton(sp =>
{
    var url = new MongoUrl(mongoUri);
    var dbName = First(url.DatabaseName, config["MongoDB:DatabaseName"], "fcg-db");
    return sp.GetRequiredService<IMongoClient>().GetDatabase(dbName);
});

// ----------------- Elasticsearch -----------------
builder.Services.Configure<ElasticOptions>(options =>
{
    // Permite configuração via appsettings ou env vars
    options.Enabled = bool.TryParse(
        First(Environment.GetEnvironmentVariable("ELASTIC_ENABLED"), config["Elastic:Enabled"]),
        out var enabled) ? enabled : false;
    
    options.Url = First(
        Environment.GetEnvironmentVariable("ELASTIC_URL"),
        config["Elastic:Url"]);
    
    options.Username = First(
        Environment.GetEnvironmentVariable("ELASTIC_USERNAME"),
        config["Elastic:Username"]);
    
    options.Password = First(
        Environment.GetEnvironmentVariable("ELASTIC_PASSWORD"),
        config["Elastic:Password"]);
    
    options.ApiKey = First(
        Environment.GetEnvironmentVariable("ELASTIC_API_KEY"),
        config["Elastic:ApiKey"]);
    
    options.IndexName = First(
        Environment.GetEnvironmentVariable("ELASTIC_INDEX_NAME"),
        config["Elastic:IndexName"],
        "games");
});

// Registra o search provider apropriado baseado na configuração
builder.Services.AddSingleton<IGameSearchProvider>(sp =>
{
    var elasticOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ElasticOptions>>().Value;
    var logger = sp.GetRequiredService<ILoggerFactory>();
    
    if (elasticOptions.IsConfigured)
    {
        return new ElasticGameSearchProvider(
            Microsoft.Extensions.Options.Options.Create(elasticOptions),
            logger.CreateLogger<ElasticGameSearchProvider>());
    }
    
    // Fallback para MongoDB quando Elasticsearch não está configurado
    var db = sp.GetRequiredService<IMongoDatabase>();
    return new FallbackMongoGameSearchProvider(db, logger.CreateLogger<FallbackMongoGameSearchProvider>());
});

// ----------------- JWT (appsettings) -----------------
string jwtKey, jwtIssuer, jwtAudience;

jwtKey = Require(
    First(
        config["JwtOptions:Key"],
        Environment.GetEnvironmentVariable("JWT_SECRET"),
        Environment.GetEnvironmentVariable("JwtOptions__Key")
    ),
    "JWT secret não configurado (JwtOptions:Key no appsettings ou env JWT_SECRET)."
);

jwtIssuer = Require(
    First(
        config["JwtOptions:Issuer"],
        Environment.GetEnvironmentVariable("JWT_ISS"),
        Environment.GetEnvironmentVariable("JwtOptions__Issuer")
    ),
    "JWT issuer não configurado (JwtOptions:Issuer no appsettings ou env JWT_ISS)."
);

jwtAudience = Require(
    First(
        config["JwtOptions:Audience"],
        Environment.GetEnvironmentVariable("JWT_AUD"),
        Environment.GetEnvironmentVariable("JwtOptions__Audience")
    ),
    "JWT audience não configurado (JwtOptions:Audience no appsettings ou env JWT_AUD)."
);

builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["JwtOptions:Key"] = jwtKey,
    ["JwtOptions:Issuer"] = jwtIssuer,
    ["JwtOptions:Audience"] = jwtAudience
});

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,

            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,

            ValidateAudience = true,
            ValidAudience = jwtAudience,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// ----------------- DI -----------------
builder.Services.AddScoped<IGameRepository, GameRepository>();
builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();
builder.Services.AddScoped<IGameService, GameService>();
builder.Services.AddScoped<IPurchaseRepository, PurchaseRepository>();
builder.Services.AddScoped<IPurchaseService, PurchaseService>();

// Outbox publisher (SQS integration events)
builder.Services.AddHostedService<Application.Services.OutboxPublisherHostedService>();

// ----------------- MVC + Swagger -----------------
builder.Services.AddControllers().AddJsonOptions(x =>
{
    x.JsonSerializerOptions.Converters.Add(new ObjectIdJsonConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "GamesSvc", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "JWT no header. Ex.: Bearer {token}",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

if (string.Equals(Environment.GetEnvironmentVariable("FCG_LOG_HTTP_BODIES"), "true", StringComparison.OrdinalIgnoreCase))
{
    app.UseMiddleware<HttpBodyLoggingMiddleware>();
}

// Para funcionar bem atrás de proxy reverso / ingress
app.UseForwardedHeaders();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ------------------------------------------------------
// Endpoints para probes do Kubernetes
// ------------------------------------------------------
app.MapGet("/health", () => Results.Ok(new
{
    ok = true,
    svc = "games",
    env = env.EnvironmentName,
    jwt = new { issuer = jwtIssuer, audience = jwtAudience }
}));

app.MapGet("/ready", () => Results.Ok(new
{
    ready = true,
    svc = "games"
}));

app.MapGet("/", () => "GamesSvc up & running (container mode)");

app.Run();
