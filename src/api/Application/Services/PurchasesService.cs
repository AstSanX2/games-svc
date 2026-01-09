using Domain.Entities;
using Domain.Events;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Application.Services
{
    public class PurchaseService(
        IGameRepository gameRepository,
        IPurchaseRepository repo,
        IEventRepository eventRepo,
        IOutboxRepository outboxRepository,
        IConfiguration configuration) : IPurchaseService
    {
        private readonly IConfiguration _configuration = configuration;
        private const string SourceName = "games-svc";

        public async Task<ObjectId> CreateAsync(ObjectId gameId, ObjectId userId, CancellationToken ct)
        {
            // 0) Bloquear compra duplicada (PAID ou PENDING)
            if (await repo.ExistsActiveAsync(userId, gameId, ct))
                throw new InvalidOperationException("Compra já realizada!");

            // 1) Calcular amount server-side (preço do jogo)
            var game = await gameRepository.GetByIdAsync<Application.DTO.GameDTO.ProjectGameDTO>(gameId);
            if (game is null)
                throw new ArgumentException("Game não encontrado");

            var amount = game.Price;

            // 1) Compra PENDING
            var p = new Purchase
            {
                UserId = userId,
                GameId = gameId,
                Checksum = ComputeChecksum(userId, gameId),
                Amount = amount,
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow
            };
            try
            {
                await repo.CreateAsync(p, ct);
            }
            catch (MongoWriteException mwe) when (mwe.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // corrida: o índice unique (UserId, GameId) protege idempotência
                throw new InvalidOperationException("Compra já realizada!");
            }

            // 2) Evento (event sourcing)
            var ev = DomainEvent.Create(
                aggregateId: p._id,
                type: "GamePurchased",
                data: new Dictionary<string, object?>
                {
                    ["UserId"] = userId,
                    ["GameId"] = gameId.ToString(),
                    ["Amount"] = amount
                }
            );

            await eventRepo.AppendEventAsync(ev, ct);

            // 3) Enfileira solicitação de pagamento via Outbox (resiliente)
            _ = EnqueuePaymentInitiatedAsync(p._id.ToString(), userId.ToString(), amount);

            return p._id;
        }

        public async Task<List<Application.DTO.GameDTO.ProjectGameDTO>> GetUserLibraryAsync(ObjectId userId, int max, CancellationToken ct)
        {
            return await repo.GetUserPaidGamesAsync(userId, max);
        }

        private static string ComputeChecksum(ObjectId userId, ObjectId gameId)
        {
            var input = $"{userId}:{gameId}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private async Task EnqueuePaymentInitiatedAsync(string purchaseId, string userId, decimal amount)
        {
            try
            {
                var queueUrl = _configuration["Sqs:PaymentsQueueUrl"] ?? _configuration["PAYMENTS_QUEUE_URL"];
                if (string.IsNullOrWhiteSpace(queueUrl)) return;

                // Use W3C traceparent to allow API -> outbox publish -> worker correlation.
                var correlationId = Activity.Current?.Id;
                var env = IntegrationEventEnvelope.Create(
                    type: "PaymentInitiated",
                    source: SourceName,
                    aggregateId: purchaseId,
                    data: new Dictionary<string, object?>
                    {
                        ["PurchaseId"] = purchaseId,
                        ["UserId"] = userId,
                        ["Amount"] = amount
                    },
                    correlationId: correlationId
                );

                var body = JsonSerializer.Serialize(env);
                var outbox = new OutboxMessage
                {
                    EventId = env.EventId,
                    EventType = env.Type,
                    Source = env.Source,
                    AggregateId = env.AggregateId,
                    CorrelationId = env.CorrelationId,
                    CausationId = env.CausationId,
                    Version = env.Version,
                    Destination = queueUrl,
                    Body = body
                };

                await outboxRepository.EnqueueAsync(outbox, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Outbox] Erro ao enfileirar PaymentInitiated {purchaseId}: {ex.Message}");
            }
        }
    }
}
