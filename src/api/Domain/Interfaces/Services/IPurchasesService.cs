using Application.DTO.GameDTO;
using MongoDB.Bson;

namespace Domain.Interfaces.Services
{
    public interface IPurchaseService
    {
        Task<ObjectId> CreateAsync(ObjectId gameId, ObjectId userId, CancellationToken ct);
        Task<List<ProjectGameDTO>> GetUserLibraryAsync(ObjectId userId, int max, CancellationToken ct);
    }
}
