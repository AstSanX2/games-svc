using Domain.Entities;
using Domain.Events;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using MongoDB.Bson;
using System.Diagnostics;
using System.Text.Json;

namespace Application.Services
{
    public class PurchaseService(
        IPurchaseRepository repo,
        IEventRepository eventRepo,
        IOutboxRepository outboxRepository,
        IConfiguration configuration) : IPurchaseService
    {
        private readonly IConfiguration _configuration = configuration;
        private const string SourceName = "games-svc";

        public async Task<ObjectId> CreateAsync(ObjectId gameId, decimal amount, ObjectId userId, CancellationToken ct)
        {
            // 1) Compra PENDING
            var p = new Purchase
            {
                UserId = userId,
                GameId = gameId,
                Amount = amount,
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow
            };
            await repo.CreateAsync(p, ct);

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

        private async Task EnqueuePaymentInitiatedAsync(string purchaseId, string userId, decimal amount)
        {
            try
            {
                var queueUrl = _configuration["Sqs:PaymentsQueueUrl"] ?? _configuration["PAYMENTS_QUEUE_URL"];
                if (string.IsNullOrWhiteSpace(queueUrl)) return;

                var correlationId = Activity.Current?.TraceId.ToString();
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
