using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Amazon.SQS;
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
        private readonly AmazonSQSClient _sqs = new();
        private readonly AmazonSimpleSystemsManagementClient _ssm = new();

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

            // 3) Publicar na fila (SQS)
            var qUrl = (await _ssm.GetParameterAsync(new GetParameterRequest
            {
                Name = "/fcg/PAYMENTS_QUEUE_URL"
            }, ct)).Parameter.Value;

            var body = JsonSerializer.Serialize(new PurchaseMsg(p._id.ToString(), userId.ToString(), amount));
            await _sqs.SendMessageAsync(qUrl, body, ct);

            return p._id;
        }
    }
}
