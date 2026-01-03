using Amazon;
using Amazon.Runtime;
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

    public class PurchaseService(IPurchaseRepository repo, IEventRepository eventRepo, IConfiguration configuration) : IPurchaseService
    {
        private readonly IAmazonSQS _sqs = CreateSqsClient(configuration);
        private readonly IConfiguration _configuration = configuration;

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

        private string? GetQueueUrl()
        {
            // 1) env var
            var queueUrl = Environment.GetEnvironmentVariable("PAYMENTS_QUEUE_URL");
            if (!string.IsNullOrEmpty(queueUrl)) return queueUrl;

            // 2) appsettings (K8s: arquivo montado; Local: arquivo do repo)
            return _configuration["Sqs:PaymentsQueueUrl"]
                ?? _configuration["PAYMENTS_QUEUE_URL"];
        }
    }
}
