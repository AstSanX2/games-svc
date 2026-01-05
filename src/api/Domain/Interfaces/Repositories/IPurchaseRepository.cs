using Application.DTO.GameDTO;
using Domain.Entities;
using MongoDB.Bson;

namespace Domain.Interfaces.Repositories
{
    public interface IPurchaseRepository
    {
        Task CreateAsync(Purchase purchase, CancellationToken ct);
        Task<List<ProjectGameDTO>> GetTopPopularAsync(int limit = 10);
        Task<List<ObjectId>> GetUserPaidGameIdsAsync(ObjectId userId, int max = 10);
    }
}
