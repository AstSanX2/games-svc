using Amazon;
using Amazon.Runtime;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using MongoDB.Bson;
using System.Text.Json;

namespace Application.Services
{
    public record PurchaseMsg(string PurchaseId, string UserId, decimal Amount);

    public class PurchaseService(IPurchaseRepository repo, IEventRepository eventRepo) : IPurchaseService
    {
        private readonly IAmazonSQS _sqs = CreateSqsClient();

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

            // 3) Publicar na fila (SQS) - fire-and-forget
            _ = PublishPurchaseAsync(p._id.ToString(), userId.ToString(), amount);

            return p._id;
        }

        private async Task PublishPurchaseAsync(string purchaseId, string userId, decimal amount)
        {
            try
            {
                var queueUrl = GetQueueUrl();
                if (string.IsNullOrEmpty(queueUrl))
                {
                    Console.WriteLine($"[SQS] Compra {purchaseId} (SQS não configurado)");
                    return;
                }

                var body = JsonSerializer.Serialize(new PurchaseMsg(purchaseId, userId, amount));
                await _sqs.SendMessageAsync(new SendMessageRequest
                {
                    QueueUrl = queueUrl,
                    MessageBody = body
                });

                Console.WriteLine($"[SQS] Compra {purchaseId} publicada na fila payments");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SQS] Erro ao publicar compra {purchaseId}: {ex.Message}");
            }
        }

        private static string? GetQueueUrl()
        {
            // 1) Primeiro tenta variável de ambiente (K8s ConfigMap/Secret ou docker-compose)
            var queueUrl = Environment.GetEnvironmentVariable("PAYMENTS_QUEUE_URL");
            if (!string.IsNullOrEmpty(queueUrl))
                return queueUrl;

            // 2) Tenta SSM (para AWS real)
            try
            {
                using var ssm = new AmazonSimpleSystemsManagementClient();
                var resp = ssm.GetParameterAsync(new GetParameterRequest
                {
                    Name = "/fcg/PAYMENTS_QUEUE_URL"
                }).GetAwaiter().GetResult();
                return resp.Parameter?.Value;
            }
            catch
            {
                return null;
            }
        }
    }
}
