using Application.DTO.GameDTO;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using Domain.Models.Response;
using MongoDB.Bson;

namespace Application.Services
{
    public class GameService : IGameService
    {
        private readonly IGameRepository _repo;
        public GameService(IGameRepository repo) => _repo = repo;

        public Task<List<ProjectGameDTO>> GetAllAsync() =>
            _repo.GetAllAsync<ProjectGameDTO>();

        public Task<ProjectGameDTO?> GetByIdAsync(ObjectId id) =>
            _repo.GetByIdAsync<ProjectGameDTO>(id);

        public Task<List<ProjectGameDTO>> FindGamesAsync(FilterGameDTO filterDto) =>
            _repo.FindAsync<ProjectGameDTO>(filterDto);

        public async Task<ResponseModel<ProjectGameDTO>> CreateAsync(CreateGameDTO createDto)
        {
            var validationResult = createDto.Validate();
            if (validationResult.HasError)
                return ResponseModel<ProjectGameDTO>.BadRequest(validationResult.ToString());

            var entity = await _repo.CreateAsync(createDto);
            var dto = await _repo.GetByIdAsync<ProjectGameDTO>(entity._id);
            return ResponseModel<ProjectGameDTO>.Created(dto);
        }

        public Task UpdateAsync(ObjectId id, UpdateGameDTO updateDto) =>
            _repo.UpdateAsync(id, updateDto);

        public Task DeleteAsync(ObjectId id) =>
            _repo.DeleteAsync(id);

        public Task<IReadOnlyList<ProjectGameSearchDTO>> SearchAsync(SearchGameDTO query) =>
            _repo.SearchAtlasAsync(query);

        public Task<IReadOnlyList<PopularGameDTO>> GetPopularAsync(int top) =>
            _repo.GetPopularAsync(top);
    }
}
