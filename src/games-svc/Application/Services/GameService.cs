using Application.DTO.GameDTO;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using Domain.Models.Response;
using MongoDB.Bson;

namespace Application.Services
{
    public class GameService(
        IGameRepository gameRepository,
        IPurchaseRepository purchaseRepository,
        IEventRepository eventRepo) : IGameService
    {
        public async Task<List<ProjectGameDTO>> GetAllAsync(CancellationToken ct = default)
        {
            var result = await gameRepository.GetAllAsync<ProjectGameDTO>();

            var ev = DomainEvent.Create(
                aggregateId: ObjectId.Empty,
                type: "GamesListed",
                data: new Dictionary<string, object?>
                {
                    ["Count"] = result?.Count ?? 0
                }
            );
            await eventRepo.AppendEventAsync(ev, ct);

            return result;
        }

        public async Task<ProjectGameDTO?> GetByIdAsync(ObjectId id, CancellationToken ct = default)
        {
            var dto = await gameRepository.GetByIdAsync<ProjectGameDTO>(id);

            var ev = DomainEvent.Create(
                aggregateId: id,
                type: dto is null ? "GameNotFound" : "GameFetched",
                data: new Dictionary<string, object?>
                {
                    ["GameId"] = id.ToString(),
                    ["Found"] = dto is not null
                }
            );
            await eventRepo.AppendEventAsync(ev, ct);

            return dto;
        }

        public async Task<List<ProjectGameDTO>> FindGamesAsync(FilterGameDTO filterDto, CancellationToken ct = default)
        {
            var result = await gameRepository.FindAsync<ProjectGameDTO>(filterDto);

            var ev = DomainEvent.Create(
                aggregateId: ObjectId.Empty,
                type: "GameFilterQueried",
                data: new Dictionary<string, object?>
                {
                    ["Filter"] = filterDto, // ok serializar objeto; ajuste se preferir só campos
                    ["Count"] = result?.Count ?? 0
                }
            );
            await eventRepo.AppendEventAsync(ev, ct);

            return result;
        }

        public async Task<ResponseModel<ProjectGameDTO>> CreateAsync(CreateGameDTO createDto, CancellationToken ct = default)
        {
            var validation = createDto.Validate();
            if (validation.HasError)
            {
                var evFail = DomainEvent.Create(
                    aggregateId: ObjectId.Empty,
                    type: "GameCreateValidationFailed",
                    data: new Dictionary<string, object?>
                    {
                        ["Errors"] = validation.ToString(),
                        ["Input"] = createDto
                    }
                );
                await eventRepo.AppendEventAsync(evFail, ct);

                return ResponseModel<ProjectGameDTO>.BadRequest(validation.ToString());
            }

            var entity = await gameRepository.CreateAsync(createDto);
            var dto = await gameRepository.GetByIdAsync<ProjectGameDTO>(entity._id);

            var ev = DomainEvent.Create(
                aggregateId: entity._id,
                type: "GameCreated",
                data: new Dictionary<string, object?>
                {
                    ["GameId"] = entity._id.ToString(),
                    ["Name"] = createDto.Name,
                    ["Category"] = createDto.Category,
                    ["ReleaseDate"] = createDto.ReleaseDate,
                    ["Price"] = createDto.Price
                }
            );
            await eventRepo.AppendEventAsync(ev, ct);

            return ResponseModel<ProjectGameDTO>.Created(dto);
        }

        public async Task UpdateAsync(ObjectId id, UpdateGameDTO updateDto, CancellationToken ct = default)
        {
            await gameRepository.UpdateAsync(id, updateDto);

            var ev = DomainEvent.Create(
                aggregateId: id,
                type: "GameUpdated",
                data: new Dictionary<string, object?>
                {
                    ["GameId"] = id.ToString(),
                    ["Changes"] = updateDto
                }
            );
            await eventRepo.AppendEventAsync(ev, ct);
        }

        public async Task DeleteAsync(ObjectId id, CancellationToken ct = default)
        {
            await gameRepository.DeleteAsync(id);

            var ev = DomainEvent.Create(
                aggregateId: id,
                type: "GameDeleted",
                data: new Dictionary<string, object?>
                {
                    ["GameId"] = id.ToString()
                }
            );
            await eventRepo.AppendEventAsync(ev, ct);
        }

        public async Task<IReadOnlyList<ProjectGameSearchDTO>> SearchAsync(SearchGameDTO query, CancellationToken ct = default)
        {
            var result = await gameRepository.SearchAtlasAsync(query);

            var ev = DomainEvent.Create(
                aggregateId: ObjectId.Empty,
                type: "GameSearchExecuted",
                data: new Dictionary<string, object?>
                {
                    ["Query"] = query,
                    ["Count"] = result?.Count ?? 0
                }
            );
            await eventRepo.AppendEventAsync(ev, ct);

            return result;
        }

        public async Task<IReadOnlyList<ProjectGameDTO>> GetPopularAsync(int top, CancellationToken ct = default)
        {
            var result = await purchaseRepository.GetTopPopularAsync(top);

            var ev = DomainEvent.Create(
                aggregateId: ObjectId.Empty,
                type: "GamePopularRequested",
                data: new Dictionary<string, object?>
                {
                    ["Top"] = top,
                    ["Count"] = result?.Count ?? 0
                }
            );
            await eventRepo.AppendEventAsync(ev, ct);

            return result;
        }

        public async Task<List<ProjectGameDTO>> GetRecommendationsAsync(ObjectId userId, int limit = 10, CancellationToken ct = default)
        {
            var purchasedIds = await purchaseRepository.GetUserPaidGameIdsAsync(userId, 10);

            if (purchasedIds.Count == 0)
            {
                // fallback: populares
                var fallback = await purchaseRepository.GetTopPopularAsync(limit);

                var evFallback = DomainEvent.Create(
                    aggregateId: userId,
                    type: "GameRecommendationsFallbackPopular",
                    data: new Dictionary<string, object?>
                    {
                        ["UserId"] = userId.ToString(),
                        ["Limit"] = limit,
                        ["PurchasedHistoryCount"] = 0,
                        ["ResultCount"] = fallback?.Count ?? 0
                    }
                );
                await eventRepo.AppendEventAsync(evFallback, ct);

                return fallback;
            }

            var recs = await gameRepository.RecommendBySimilarAsync(purchasedIds, purchasedIds, limit);

            var ev = DomainEvent.Create(
                aggregateId: userId,
                type: "GameRecommendationsGenerated",
                data: new Dictionary<string, object?>
                {
                    ["UserId"] = userId.ToString(),
                    ["Limit"] = limit,
                    ["PurchasedHistoryCount"] = purchasedIds.Count,
                    ["LikeIds"] = purchasedIds.ConvertAll(x => x.ToString()),
                    ["ResultCount"] = recs?.Count ?? 0
                }
            );
            await eventRepo.AppendEventAsync(ev, ct);

            return recs;
        }
    }
}
