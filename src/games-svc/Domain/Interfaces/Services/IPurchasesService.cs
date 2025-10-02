using MongoDB.Bson;

namespace Domain.Interfaces.Services
{
    public interface IPurchaseService
    {
        Task<ObjectId> CreateAsync(ObjectId gameId, decimal amount, ObjectId userId, CancellationToken ct);
    }
}
