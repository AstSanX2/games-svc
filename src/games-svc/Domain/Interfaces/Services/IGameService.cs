using Application.DTO.GameDTO;
using Domain.Models.Response;
using MongoDB.Bson;

namespace Domain.Interfaces.Services
{
    public interface IGameService
    {
        Task<List<ProjectGameDTO>> GetAllAsync();
        Task<ProjectGameDTO?> GetByIdAsync(ObjectId id);
        Task<List<ProjectGameDTO>> FindGamesAsync(FilterGameDTO filterDto);
        Task<ResponseModel<ProjectGameDTO>> CreateAsync(CreateGameDTO createDto);
        Task UpdateAsync(ObjectId id, UpdateGameDTO updateDto);
        Task DeleteAsync(ObjectId id);

        // Novos (obrigatórios)
        Task<IReadOnlyList<ProjectGameSearchDTO>> SearchAsync(SearchGameDTO query);
        Task<IReadOnlyList<ProjectGameDTO>> GetPopularAsync(int top);
        Task<List<ProjectGameDTO>> GetRecommendationsAsync(ObjectId userId, int limit = 10);
    }
}
