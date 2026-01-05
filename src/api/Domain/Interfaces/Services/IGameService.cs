using Application.DTO.GameDTO;
using Domain.Models.Response;
using MongoDB.Bson;

namespace Domain.Interfaces.Services
{
    public interface IGameService
    {
        Task<List<ProjectGameDTO>> GetAllAsync(CancellationToken ct = default);
        Task<ProjectGameDTO?> GetByIdAsync(ObjectId id, CancellationToken ct = default);
        Task<List<ProjectGameDTO>> FindGamesAsync(FilterGameDTO filterDto, CancellationToken ct = default);
        Task<ResponseModel<ProjectGameDTO>> CreateAsync(CreateGameDTO createDto, CancellationToken ct = default);
        Task UpdateAsync(ObjectId id, UpdateGameDTO updateDto, CancellationToken ct = default);
        Task DeleteAsync(ObjectId id, CancellationToken ct = default);
        Task<IReadOnlyList<ProjectGameSearchDTO>> SearchAsync(SearchGameDTO query, CancellationToken ct = default);
        Task<IReadOnlyList<ProjectGameDTO>> GetPopularAsync(int top, CancellationToken ct = default);
        Task<List<ProjectGameDTO>> GetRecommendationsAsync(ObjectId userId, int limit = 10, CancellationToken ct = default);
        Task<ResponseModel<bool>> StartGameAsync(ObjectId gameId, ObjectId userId, CancellationToken ct = default);
        Task<ResponseModel<bool>> QueueGameAsync(ObjectId gameId, ObjectId userId, CancellationToken ct = default);
    }
}
