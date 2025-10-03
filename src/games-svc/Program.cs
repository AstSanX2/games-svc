using Amazon;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Application.Services;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using Helpers;
using Infraestructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using System.Net;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Executará como Lambda atrás do API Gateway (REST)
builder.Services.AddAWSLambdaHosting(LambdaEventSource.RestApi);

var env = builder.Environment;
var config = builder.Configuration;

bool useSsm = !env.IsDevelopment() ||
    string.Equals(Environment.GetEnvironmentVariable("USE_SSM"), "true", StringComparison.OrdinalIgnoreCase);

// SSM client na região da Lambda
var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
var ssm = new AmazonSimpleSystemsManagementClient(RegionEndpoint.GetBySystemName(region));

string? GetSsm(string name, bool decrypt = true)
{
    try
    {
        var r = ssm.GetParameterAsync(new GetParameterRequest { Name = name, WithDecryption = decrypt })
                   .GetAwaiter().GetResult();
        return r.Parameter.Value;
    }
    catch (ParameterNotFoundException) { return null; }
    catch (AmazonSimpleSystemsManagementException ex) when (
        ex.ErrorCode == "UnrecognizedClientException" || ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
    { return null; }
}

// ---------- MongoDB ----------
var mongoUri =
    (useSsm ? GetSsm("/fcg/MONGODB_URI") : null)
    ?? config["MongoDB:ConnectionString"]
    ?? GetSsm("/fcg/MONGODB_URI")
    ?? throw new InvalidOperationException("MongoDB URI not found in SSM (/fcg/MONGODB_URI) or appsettings.");

builder.Services.AddSingleton<IMongoClient>(_ =>
{
    var settings = MongoClientSettings.FromConnectionString(mongoUri);
    settings.ServerApi = new ServerApi(ServerApiVersion.V1);
    return new MongoClient(settings);
});

builder.Services.AddSingleton(sp =>
{
    var url = new MongoUrl(mongoUri);
    var dbName = url.DatabaseName;
    if (string.IsNullOrWhiteSpace(dbName))
        dbName = config["MongoDB:DatabaseName"] ?? "fgc-db"; // fallback
    return sp.GetRequiredService<IMongoClient>().GetDatabase(dbName);
});

// ---------- JWT ----------
var jwtSecret =
    (useSsm ? GetSsm("/fcg/JWT_SECRET") : null)
    ?? config["JwtOptions:Key"]
    ?? GetSsm("/fcg/JWT_SECRET")
    ?? throw new InvalidOperationException("JWT_SECRET not found in SSM (/fcg/JWT_SECRET) or appsettings.");

var jwtIssuer =
    (useSsm ? GetSsm("/fcg/JWT_ISS", decrypt: false) : null)
    ?? config["JwtOptions:Issuer"]
    ?? GetSsm("/fcg/JWT_ISS", decrypt: false)
    ?? throw new InvalidOperationException("JWT_ISS not found in SSM (/fcg/JWT_ISS) or appsettings.");

var jwtAudience =
    (useSsm ? GetSsm("/fcg/JWT_AUD", decrypt: false) : null)
    ?? config["JwtOptions:Audience"]
    ?? GetSsm("/fcg/JWT_AUD", decrypt: false)
    ?? throw new InvalidOperationException("JWT_AUD not found in SSM (/fcg/JWT_AUD) or appsettings.");

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,

            ValidateIssuer = !string.IsNullOrWhiteSpace(jwtIssuer),
            ValidIssuer = string.IsNullOrWhiteSpace(jwtIssuer) ? null : jwtIssuer,

            ValidateAudience = !string.IsNullOrWhiteSpace(jwtAudience),
            ValidAudience = string.IsNullOrWhiteSpace(jwtAudience) ? null : jwtAudience,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// ---------- DI ----------
builder.Services.AddScoped<IGameRepository, GameRepository>();
builder.Services.AddScoped<IGameService, GameService>();

builder.Services.AddScoped<IPurchaseRepository, PurchaseRepository>();
builder.Services.AddScoped<IPurchaseService, PurchaseService>();

// ---------- MVC + Swagger ----------
builder.Services.AddControllers().AddJsonOptions(x =>
{
    x.JsonSerializerOptions.Converters.Add(new ObjectIdJsonConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "GamesSvc", Version = "v1" });

    // Swagger com Bearer (para "Authorize" no UI)
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

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { ok = true, svc = "games" }));

app.Run();