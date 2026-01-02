using Amazon;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Application.Services;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using Helpers;
using Infraestructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using System.Net;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

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

bool useSsm = !env.IsDevelopment() ||
              string.Equals(Environment.GetEnvironmentVariable("USE_SSM"), "true", StringComparison.OrdinalIgnoreCase);

// ----------------- Fun��es utilit�rias -----------------
static string Require(string? v, string error) =>
    string.IsNullOrWhiteSpace(v) ? throw new InvalidOperationException(error) : v;

static string First(params string?[] vals) =>
    vals.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

string? GetSsm(string name, bool decrypt = true)
{
    try
    {
        var regionName = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
        using var ssm = new AmazonSimpleSystemsManagementClient(RegionEndpoint.GetBySystemName(regionName));
        var r = ssm.GetParameterAsync(new GetParameterRequest { Name = name, WithDecryption = decrypt })
                   .GetAwaiter().GetResult();
        return r.Parameter?.Value;
    }
    catch (ParameterNotFoundException) { return null; }
    catch (AmazonSimpleSystemsManagementException ex) when (
        ex.ErrorCode == "UnrecognizedClientException" ||
        ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
    { return null; }
}

// ----------------- MongoDB -----------------
var mongoUri = useSsm
    ? Require(GetSsm("/fcg/MONGODB_URI"), "MongoDB URI n�o encontrada no SSM (/fcg/MONGODB_URI).")
    : Require(config["MongoDB:ConnectionString"], "MongoDB:ConnectionString ausente no appsettings (Development).");

builder.Services.AddSingleton<IMongoClient>(_ =>
{
    var settings = MongoClientSettings.FromConnectionString(mongoUri);
    settings.ServerApi = new ServerApi(ServerApiVersion.V1);
    return new MongoClient(settings);
});

builder.Services.AddSingleton(sp =>
{
    var url = new MongoUrl(mongoUri);
    var dbName = First(url.DatabaseName, config["MongoDB:DatabaseName"], "fgc-db");
    return sp.GetRequiredService<IMongoClient>().GetDatabase(dbName);
});

// ----------------- JWT (SSM fora de Dev; appsettings em Dev) -----------------
string jwtKey, jwtIssuer, jwtAudience;

if (useSsm)
{
    jwtKey = Require(GetSsm("/fcg/JWT_SECRET"), "SSM /fcg/JWT_SECRET ausente.");
    jwtIssuer = Require(GetSsm("/fcg/JWT_ISS", decrypt: false), "SSM /fcg/JWT_ISS ausente.");
    jwtAudience = Require(GetSsm("/fcg/JWT_AUD", decrypt: false), "SSM /fcg/JWT_AUD ausente.");
}
else
{
    jwtKey = Require(config["JwtOptions:Key"], "JwtOptions:Key ausente no appsettings (Development).");
    jwtIssuer = Require(config["JwtOptions:Issuer"], "JwtOptions:Issuer ausente no appsettings (Development).");
    jwtAudience = Require(config["JwtOptions:Audience"], "JwtOptions:Audience ausente no appsettings (Development).");
}

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
builder.Services.AddScoped<IGameService, GameService>();
builder.Services.AddScoped<IPurchaseRepository, PurchaseRepository>();
builder.Services.AddScoped<IPurchaseService, PurchaseService>();

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
    useSsm,
    jwt = new { issuer = jwtIssuer, audience = jwtAudience }
}));

app.MapGet("/ready", () => Results.Ok(new
{
    ready = true,
    svc = "games"
}));

app.MapGet("/", () => "GamesSvc up & running (container mode)");

app.Run();
