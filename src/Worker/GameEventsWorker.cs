using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace GamesWorker;

public record IntegrationEventEnvelope(
    Guid EventId,
    string Type,
    DateTime OccurredAt,
    string Source,
    string AggregateId,
    string? CorrelationId,
    string? CausationId,
    int Version,
    JsonElement Data
);

public class GameEventsWorker : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("games-worker");
    private readonly IMongoDatabase _db;
    private readonly IAmazonSQS _sqs;
    private readonly string _queueUrl;
    private readonly int _pollIntervalMs;
    private readonly int _maxMessages;

    public GameEventsWorker(IMongoDatabase db, IConfiguration configuration)
    {
        _db = db;
        _sqs = CreateSqsClient(configuration);
        var queueUrl = configuration["Sqs:GamesEventsQueueUrl"]
            ?? configuration["GAMES_EVENTS_QUEUE_URL"]
            ?? Environment.GetEnvironmentVariable("GAMES_EVENTS_QUEUE_URL");

        if (string.IsNullOrWhiteSpace(queueUrl))
            throw new InvalidOperationException("Games queue URL not configured (defina Sqs:GamesEventsQueueUrl no appsettings ou a env GAMES_EVENTS_QUEUE_URL).");

        _queueUrl = queueUrl;

        _pollIntervalMs = int.TryParse(configuration["Worker:PollIntervalMs"] ?? configuration["POLL_INTERVAL_MS"], out var interval)
            ? interval : 5000;
        _maxMessages = int.TryParse(configuration["Worker:MaxMessages"] ?? configuration["MAX_MESSAGES"], out var max)
            ? max : 10;

        EnsureIdempotencyIndex();
    }

    private void EnsureIdempotencyIndex()
    {
        var events = _db.GetCollection<BsonDocument>("Events");
        var indexKeys = Builders<BsonDocument>.IndexKeys.Ascending("SqsMessageId");
        var index = new CreateIndexModel<BsonDocument>(indexKeys, new CreateIndexOptions { Unique = true, Name = "ux_sqsMessageId" });
        try
        {
            events.Indexes.CreateOne(index);
        }
        catch
        {
            // best-effort
        }
    }

    private static IAmazonSQS CreateSqsClient(IConfiguration configuration)
    {
        var serviceUrl = configuration["Sqs:ServiceUrl"] ?? Environment.GetEnvironmentVariable("SQS_SERVICE_URL");
        if (!string.IsNullOrEmpty(serviceUrl))
        {
            // LocalStack ou outro emulador
            var config = new AmazonSQSConfig { ServiceURL = serviceUrl };
            var accessKey = configuration["AWS:AccessKey"];
            var secretKey = configuration["AWS:SecretKey"];
            if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey))
                return new AmazonSQSClient(new BasicAWSCredentials(accessKey, secretKey), config);

            return new AmazonSQSClient(new BasicAWSCredentials("test", "test"), config);
        }
        // AWS real (credenciais via appsettings ou cadeia default)
        var region = configuration["AWS:Region"] ?? Environment.GetEnvironmentVariable("AWS_REGION");
        var sqsConfig = new AmazonSQSConfig();
        if (!string.IsNullOrWhiteSpace(region))
            sqsConfig.RegionEndpoint = RegionEndpoint.GetBySystemName(region);

        var ak = configuration["AWS:AccessKey"];
        var sk = configuration["AWS:SecretKey"];
        if (!string.IsNullOrWhiteSpace(ak) && !string.IsNullOrWhiteSpace(sk))
            return new AmazonSQSClient(new BasicAWSCredentials(ak, sk), sqsConfig);

        return new AmazonSQSClient(sqsConfig);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine($"[GamesWorker] Escutando fila: {_queueUrl}");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pollActivity = ActivitySource.StartActivity("sqs receive", ActivityKind.Consumer);
                pollActivity?.SetTag("messaging.system", "aws.sqs");
                pollActivity?.SetTag("messaging.destination", "games-events-queue");
                pollActivity?.SetTag("messaging.operation", "receive");

                var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = _queueUrl,
                    MaxNumberOfMessages = _maxMessages,
                    WaitTimeSeconds = 20, // Long polling
                    VisibilityTimeout = 60
                }, stoppingToken);

                var messages = response?.Messages;
                if (messages is null || messages.Count == 0)
                {
                    await Task.Delay(_pollIntervalMs, stoppingToken);
                    continue;
                }

                foreach (var message in messages)
                {
                    if (message is null)
                        continue;

                    try
                    {
                        using var consumeActivity = StartConsumerActivityFromBody(message);
                        await ProcessMessageAsync(message, stoppingToken);
                        await _sqs.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                        Console.WriteLine($"[GamesWorker] Mensagem processada: {message.MessageId}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GamesWorker] Erro ao processar mensagem {message.MessageId}: {ex.Message}");
                        // Mensagem volta para a fila após visibility timeout
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GamesWorker] Erro no loop: {ex}");
                await Task.Delay(5000, stoppingToken);
            }
        }

        Console.WriteLine("[GamesWorker] Worker encerrado");
    }

    private async Task ProcessMessageAsync(Message message, CancellationToken ct)
    {
        var env = JsonSerializer.Deserialize<IntegrationEventEnvelope>(message.Body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (env is null)
            throw new InvalidOperationException("Envelope inválido (null).");

        var (aggregateId, userId) = ExtractAggregateAndUser(env);

        Console.WriteLine($"[GamesWorker] Processando evento {env.Type} (aggregate: {aggregateId})");

        var events = _db.GetCollection<BsonDocument>("Events");

        // Grava evento processado no MongoDB com MessageId para idempotência
        var doc = new BsonDocument
        {
            { "SqsMessageId", message.MessageId },
            { "AggregateId", aggregateId },
            { "Type", $"{env.Type}Processed" },
            { "Timestamp", DateTime.UtcNow },
            { "Data", new BsonDocument
                {
                    { "EventId", env.EventId.ToString() },
                    { "OriginalEventType", env.Type },
                    { "AggregateId", aggregateId },
                    { "UserId", string.IsNullOrWhiteSpace(userId) ? BsonNull.Value : userId },
                    { "OriginalTimestamp", env.OccurredAt },
                    { "Source", env.Source },
                    { "CorrelationId", env.CorrelationId is null ? BsonNull.Value : env.CorrelationId },
                    { "ProcessedAt", DateTime.UtcNow }
                }
            }
        };

        try
        {
            await events.InsertOneAsync(doc, cancellationToken: ct);
        }
        catch (MongoWriteException mw) when (mw.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            Console.WriteLine($"[GamesWorker] Mensagem {message.MessageId} já processada (duplicate key), ignorando");
            return;
        }

        // Lógica adicional baseada no tipo de evento
        switch (env.Type)
        {
            case "GameStarted":
                await HandleGameStartedAsync(aggregateId, userId, ct);
                break;
            case "GameQueued":
                await HandleGameQueuedAsync(aggregateId, userId, ct);
                break;
            case "CreateGameRequested":
                await HandleCreateGameRequestedAsync(env, userId, ct);
                break;
            default:
                Console.WriteLine($"[GamesWorker] Tipo de evento desconhecido: {env.Type}");
                break;
        }
    }

    private static (string aggregateId, string userId) ExtractAggregateAndUser(IntegrationEventEnvelope env)
    {
        if (string.Equals(env.Type, "CreateGameRequested", StringComparison.OrdinalIgnoreCase))
        {
            if (!env.Data.TryGetProperty("UserId", out var userIdEl) || userIdEl.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("Envelope sem Data.UserId.");

            var userId = userIdEl.GetString() ?? "";

            if (env.Data.TryGetProperty("Name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                var name = (nameEl.GetString() ?? "").Trim();
                return (string.IsNullOrWhiteSpace(name) ? env.AggregateId : name, userId);
            }

            return (env.AggregateId, userId);
        }

        if (!env.Data.TryGetProperty("GameId", out var gameIdEl) || gameIdEl.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Envelope sem Data.GameId.");

        if (!env.Data.TryGetProperty("UserId", out var userIdEl2) || userIdEl2.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Envelope sem Data.UserId.");

        var gameId = gameIdEl.GetString() ?? "";
        var userId2 = userIdEl2.GetString() ?? "";
        return (gameId, userId2);
    }

    private static Activity? StartConsumerActivity(IntegrationEventEnvelope env, Message message)
    {
        Activity? activity;

        if (!string.IsNullOrWhiteSpace(env.CorrelationId)
            && ActivityContext.TryParse(env.CorrelationId, null, out var parentContext))
        {
            activity = ActivitySource.StartActivity($"{env.Type} consume", ActivityKind.Consumer, parentContext);
        }
        else
        {
            activity = ActivitySource.StartActivity($"{env.Type} consume", ActivityKind.Consumer);
        }

        activity?.SetTag("messaging.system", "aws.sqs");
        activity?.SetTag("messaging.destination", "games-events-queue");
        activity?.SetTag("messaging.operation", "process");
        activity?.SetTag("messaging.message_id", message.MessageId);
        activity?.SetTag("fcg.event_type", env.Type);
        activity?.SetTag("fcg.source", env.Source);
        activity?.SetTag("fcg.aggregate_id", env.AggregateId);

        return activity;
    }

    private static Activity? StartConsumerActivityFromBody(Message message)
    {
        try
        {
            var env = JsonSerializer.Deserialize<IntegrationEventEnvelope>(message.Body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (env is null)
                return ActivitySource.StartActivity("sqs message consume", ActivityKind.Consumer);

            return StartConsumerActivity(env, message);
        }
        catch
        {
            var activity = ActivitySource.StartActivity("sqs message consume", ActivityKind.Consumer);
            activity?.SetTag("messaging.system", "aws.sqs");
            activity?.SetTag("messaging.destination", "games-events-queue");
            activity?.SetTag("messaging.operation", "process");
            activity?.SetTag("messaging.message_id", message.MessageId);
            return activity;
        }
    }

    private async Task HandleGameStartedAsync(string gameId, string userId, CancellationToken ct)
    {
        // Atualiza estatísticas de jogo ou tracking de sessão
        // Collection name deve ser "Game" para coincidir com o repositório (nameof(Game))
        var games = _db.GetCollection<BsonDocument>("Game");
        if (ObjectId.TryParse(gameId, out var oid))
        {
            var filter = Builders<BsonDocument>.Filter.Eq("_id", oid);
            var update = Builders<BsonDocument>.Update
                .Inc("PlayCount", 1)
                .Set("LastPlayedAt", DateTime.UtcNow);
            await games.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
        Console.WriteLine($"[GamesWorker] GameStarted processado: {gameId} por usuário {userId}");
    }

    private async Task HandleGameQueuedAsync(string gameId, string userId, CancellationToken ct)
    {
        // Registra jogo na fila do usuário ou notificação
        Console.WriteLine($"[GamesWorker] GameQueued processado: {gameId} por usuário {userId}");
        await Task.CompletedTask;
    }

    private async Task HandleCreateGameRequestedAsync(IntegrationEventEnvelope env, string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidOperationException("CreateGameRequested sem UserId.");

        if (!env.Data.TryGetProperty("Name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("CreateGameRequested sem Name.");

        if (!env.Data.TryGetProperty("Description", out var descriptionEl) || descriptionEl.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("CreateGameRequested sem Description.");

        if (!env.Data.TryGetProperty("Category", out var categoryEl) || categoryEl.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("CreateGameRequested sem Category.");

        if (!env.Data.TryGetProperty("ReleaseDate", out var releaseDateEl) || releaseDateEl.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("CreateGameRequested sem ReleaseDate.");

        if (!env.Data.TryGetProperty("Price", out var priceEl))
            throw new InvalidOperationException("CreateGameRequested sem Price.");

        var name = (nameEl.GetString() ?? "").Trim();
        var description = (descriptionEl.GetString() ?? "").Trim();
        var category = (categoryEl.GetString() ?? "").Trim();
        var releaseDateStr = releaseDateEl.GetString() ?? "";

        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("CreateGameRequested com Name vazio.");

        if (string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException("CreateGameRequested com Description vazio.");

        if (string.IsNullOrWhiteSpace(category))
            throw new InvalidOperationException("CreateGameRequested com Category vazio.");

        if (!DateTime.TryParse(releaseDateStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var releaseDate))
            throw new InvalidOperationException("CreateGameRequested com ReleaseDate inválido.");

        decimal price;
        if (priceEl.ValueKind == JsonValueKind.Number)
        {
            price = priceEl.GetDecimal();
        }
        else if (priceEl.ValueKind == JsonValueKind.String && decimal.TryParse(priceEl.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            price = parsed;
        }
        else
        {
            throw new InvalidOperationException("CreateGameRequested com Price inválido.");
        }

        if (price < 0)
            throw new InvalidOperationException("CreateGameRequested com Price negativo.");

        var games = _db.GetCollection<BsonDocument>("Game");

        // Ignora se já existir jogo com mesmo nome (case-insensitive)
        var escaped = Regex.Escape(name);
        var existsFilter = Builders<BsonDocument>.Filter.Regex("Name", new BsonRegularExpression($"^{escaped}$", "i"));
        var exists = await games.Find(existsFilter).Limit(1).AnyAsync(ct);
        if (exists)
        {
            Console.WriteLine($"[GamesWorker] CreateGameRequested ignorado (já existe): {name}");
            return;
        }

        var doc = new BsonDocument
        {
            { "Name", name },
            { "Description", description },
            { "Category", category },
            { "ReleaseDate", releaseDate },
            { "LastUpdateDate", BsonNull.Value },
            { "Price", price },
            { "CreatedByUserId", userId },
            { "CreatedAt", DateTime.UtcNow }
        };

        await games.InsertOneAsync(doc, cancellationToken: ct);
        Console.WriteLine($"[GamesWorker] Jogo criado via fila: {name}");
    }
}

