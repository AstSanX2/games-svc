using Application.DTO.GameDTO;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using Domain.Models.Response;
using MongoDB.Bson;

namespace Application.Services
{
    public class GameService(IGameRepository gameRepository, IPurchaseRepository purchaseRepository) : IGameService
    {
        public Task<List<ProjectGameDTO>> GetAllAsync() =>
            gameRepository.GetAllAsync<ProjectGameDTO>();

        public Task<ProjectGameDTO?> GetByIdAsync(ObjectId id) =>
            gameRepository.GetByIdAsync<ProjectGameDTO>(id);

        public Task<List<ProjectGameDTO>> FindGamesAsync(FilterGameDTO filterDto) =>
            gameRepository.FindAsync<ProjectGameDTO>(filterDto);

        public async Task<ResponseModel<ProjectGameDTO>> CreateAsync(CreateGameDTO createDto)
        {
            var validationResult = createDto.Validate();
            if (validationResult.HasError)
                return ResponseModel<ProjectGameDTO>.BadRequest(validationResult.ToString());

            var entity = await gameRepository.CreateAsync(createDto);
            var dto = await gameRepository.GetByIdAsync<ProjectGameDTO>(entity._id);
            return ResponseModel<ProjectGameDTO>.Created(dto);
        }

        public async Task UpdateAsync(ObjectId id, UpdateGameDTO updateDto) =>
            await gameRepository.UpdateAsync(id, updateDto);

        public async Task DeleteAsync(ObjectId id) =>
            await gameRepository.DeleteAsync(id);

        public async Task<IReadOnlyList<ProjectGameSearchDTO>> SearchAsync(SearchGameDTO query) =>
            await gameRepository.SearchAtlasAsync(query);

        public async Task<IReadOnlyList<ProjectGameDTO>> GetPopularAsync(int top) =>
            await purchaseRepository.GetTopPopularAsync(top);

        public async Task<List<ProjectGameDTO>> GetRecommendationsAsync(ObjectId userId, int limit = 10)
        {
            // últimos N jogos comprados (PAID)
            var purchasedIds = await purchaseRepository.GetUserPaidGameIdsAsync(userId, 10);

            if (purchasedIds.Count == 0)
            {
                // Sem histórico: populares
                return await purchaseRepository.GetTopPopularAsync(limit);
            }

            // Recomendar por similaridade (Atlas Search: moreLikeThis), excluindo os já comprados
            return await gameRepository.RecommendBySimilarAsync(purchasedIds, purchasedIds, limit);
        }
    }
}
