using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

namespace GamesWorker;

public record GameEventMessage(string EventType, string GameId, string UserId, DateTime Timestamp, Dictionary<string, object>? Data = null);

public class GameEventsWorker : BackgroundService
{
    private readonly IMongoDatabase _db;
    private readonly IAmazonSQS _sqs;
    private readonly string _queueUrl;
    private readonly int _pollIntervalMs;
    private readonly int _maxMessages;

    public GameEventsWorker(IMongoDatabase db)
    {
        _db = db;
        _sqs = CreateSqsClient();
        _queueUrl = Environment.GetEnvironmentVariable("GAMES_EVENTS_QUEUE_URL")
            ?? throw new InvalidOperationException("GAMES_EVENTS_QUEUE_URL não configurada");
        _pollIntervalMs = int.TryParse(Environment.GetEnvironmentVariable("POLL_INTERVAL_MS"), out var interval)
            ? interval : 5000;
        _maxMessages = int.TryParse(Environment.GetEnvironmentVariable("MAX_MESSAGES"), out var max)
            ? max : 10;
    }

    private static IAmazonSQS CreateSqsClient()
    {
        var serviceUrl = Environment.GetEnvironmentVariable("SQS_SERVICE_URL");
        if (!string.IsNullOrEmpty(serviceUrl))
        {
            // LocalStack ou outro emulador
            var config = new AmazonSQSConfig { ServiceURL = serviceUrl };
            return new AmazonSQSClient(new BasicAWSCredentials("test", "test"), config);
        }
        // AWS real
        return new AmazonSQSClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine($"[GamesWorker] Escutando fila: {_queueUrl}");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = _queueUrl,
                    MaxNumberOfMessages = _maxMessages,
                    WaitTimeSeconds = 20, // Long polling
                    VisibilityTimeout = 60
                }, stoppingToken);

                if (response.Messages.Count == 0)
                {
                    await Task.Delay(_pollIntervalMs, stoppingToken);
                    continue;
                }

                foreach (var message in response.Messages)
                {
                    try
                    {
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
                Console.WriteLine($"[GamesWorker] Erro no loop: {ex.Message}");
                await Task.Delay(5000, stoppingToken);
            }
        }

        Console.WriteLine("[GamesWorker] Worker encerrado");
    }

    private async Task ProcessMessageAsync(Message message, CancellationToken ct)
    {
        var evt = JsonSerializer.Deserialize<GameEventMessage>(message.Body);
        if (evt is null)
        {
            Console.WriteLine($"[GamesWorker] Mensagem inválida: {message.Body}");
            return;
        }

        Console.WriteLine($"[GamesWorker] Processando evento {evt.EventType} para game {evt.GameId}");

        var events = _db.GetCollection<BsonDocument>("Events");

        // Idempotência: verifica se já processou este MessageId
        var existingEvent = await events.Find(
            Builders<BsonDocument>.Filter.Eq("SqsMessageId", message.MessageId)
        ).FirstOrDefaultAsync(ct);

        if (existingEvent != null)
        {
            Console.WriteLine($"[GamesWorker] Mensagem {message.MessageId} já processada, ignorando");
            return;
        }

        // Grava evento processado no MongoDB com MessageId para idempotência
        var doc = new BsonDocument
        {
            { "SqsMessageId", message.MessageId },
            { "AggregateId", evt.GameId },
            { "Type", $"{evt.EventType}Processed" },
            { "Timestamp", DateTime.UtcNow },
            { "Data", new BsonDocument
                {
                    { "OriginalEventType", evt.EventType },
                    { "GameId", evt.GameId },
                    { "UserId", evt.UserId },
                    { "OriginalTimestamp", evt.Timestamp },
                    { "ProcessedAt", DateTime.UtcNow }
                }
            }
        };

        await events.InsertOneAsync(doc, cancellationToken: ct);

        // Lógica adicional baseada no tipo de evento
        switch (evt.EventType)
        {
            case "GameStarted":
                await HandleGameStartedAsync(evt, ct);
                break;
            case "GameQueued":
                await HandleGameQueuedAsync(evt, ct);
                break;
            default:
                Console.WriteLine($"[GamesWorker] Tipo de evento desconhecido: {evt.EventType}");
                break;
        }
    }

    private async Task HandleGameStartedAsync(GameEventMessage evt, CancellationToken ct)
    {
        // Atualiza estatísticas de jogo ou tracking de sessão
        var games = _db.GetCollection<BsonDocument>("Games");
        if (ObjectId.TryParse(evt.GameId, out var gameId))
        {
            var filter = Builders<BsonDocument>.Filter.Eq("_id", gameId);
            var update = Builders<BsonDocument>.Update
                .Inc("PlayCount", 1)
                .Set("LastPlayedAt", DateTime.UtcNow);
            await games.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
        Console.WriteLine($"[GamesWorker] GameStarted processado: {evt.GameId} por usuário {evt.UserId}");
    }

    private async Task HandleGameQueuedAsync(GameEventMessage evt, CancellationToken ct)
    {
        // Registra jogo na fila do usuário ou notificação
        Console.WriteLine($"[GamesWorker] GameQueued processado: {evt.GameId} por usuário {evt.UserId}");
        await Task.CompletedTask;
    }
}

