using Application.DTO.GameDTO;
using Domain.Entities;
using Domain.Events;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Search;
using Domain.Interfaces.Services;
using Domain.Models.Response;
using MongoDB.Bson;
using System.Diagnostics;
using System.Text.Json;

namespace Application.Services
{
    public class GameService(
        IGameRepository gameRepository,
        IPurchaseRepository purchaseRepository,
        IEventRepository eventRepo,
        IOutboxRepository outboxRepository,
        IGameSearchProvider searchProvider,
        IConfiguration configuration) : IGameService
    {
        private readonly IConfiguration _configuration = configuration;
        private const string SourceName = "games-svc";

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
                    ["Filter"] = filterDto,
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

            // Indexar no Elasticsearch (best-effort, não falha a request)
            if (dto != null)
            {
                _ = searchProvider.UpsertAsync(dto, ct);
            }

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

            // Reindexar no Elasticsearch (best-effort)
            var dto = await gameRepository.GetByIdAsync<ProjectGameDTO>(id);
            if (dto != null)
            {
                _ = searchProvider.UpsertAsync(dto, ct);
            }

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

            // Remover do índice Elasticsearch (best-effort)
            _ = searchProvider.DeleteAsync(id.ToString(), ct);

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
            // Usa o search provider (Elasticsearch ou fallback Mongo)
            var result = await searchProvider.SearchAsync(query, ct);

            var ev = DomainEvent.Create(
                aggregateId: ObjectId.Empty,
                type: "GameSearchExecuted",
                data: new Dictionary<string, object?>
                {
                    ["Query"] = query,
                    ["Count"] = result?.Count ?? 0,
                    ["Provider"] = searchProvider.GetType().Name
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
                // fallback: populares (sem histórico de compras)
                var fallback = await purchaseRepository.GetTopPopularAsync(limit);

                var evFallback = DomainEvent.Create(
                    aggregateId: userId,
                    type: "GameRecommendationsFallbackPopular",
                    data: new Dictionary<string, object?>
                    {
                        ["UserId"] = userId.ToString(),
                        ["Limit"] = limit,
                        ["PurchasedHistoryCount"] = 0,
                        ["ResultCount"] = fallback?.Count ?? 0,
                        ["Reason"] = "NoHistory"
                    }
                );
                await eventRepo.AppendEventAsync(evFallback, ct);

                return fallback;
            }

            // Usa o search provider (Elasticsearch MLT ou fallback Mongo)
            var recs = await searchProvider.RecommendAsync(purchasedIds, purchasedIds, limit, ct);

            // Se o provider não retornar resultados, fallback para populares
            if (recs.Count == 0)
            {
                var fallback = await purchaseRepository.GetTopPopularAsync(limit);

                var evFallback = DomainEvent.Create(
                    aggregateId: userId,
                    type: "GameRecommendationsFallbackPopular",
                    data: new Dictionary<string, object?>
                    {
                        ["UserId"] = userId.ToString(),
                        ["Limit"] = limit,
                        ["PurchasedHistoryCount"] = purchasedIds.Count,
                        ["ResultCount"] = fallback?.Count ?? 0,
                        ["Reason"] = "NoMLTResults",
                        ["Provider"] = searchProvider.GetType().Name
                    }
                );
                await eventRepo.AppendEventAsync(evFallback, ct);

                return fallback;
            }

            var ev = DomainEvent.Create(
                aggregateId: userId,
                type: "GameRecommendationsGenerated",
                data: new Dictionary<string, object?>
                {
                    ["UserId"] = userId.ToString(),
                    ["Limit"] = limit,
                    ["PurchasedHistoryCount"] = purchasedIds.Count,
                    ["LikeIds"] = purchasedIds.ConvertAll(x => x.ToString()),
                    ["ResultCount"] = recs.Count,
                    ["Provider"] = searchProvider.GetType().Name
                }
            );
            await eventRepo.AppendEventAsync(ev, ct);

            return recs.ToList();
        }

        public async Task<ResponseModel<bool>> StartGameAsync(ObjectId gameId, ObjectId userId, CancellationToken ct = default)
        {
            var game = await gameRepository.GetByIdAsync<ProjectGameDTO>(gameId);
            if (game is null)
                return ResponseModel<bool>.NotFound("Jogo não encontrado");

            // Registra evento local
            var ev = DomainEvent.Create(
                aggregateId: gameId,
                type: "GameStarted",
                data: new Dictionary<string, object?>
                {
                    ["GameId"] = gameId.ToString(),
                    ["UserId"] = userId.ToString(),
                    ["GameName"] = game.Name
                }
            );
            await eventRepo.AppendEventAsync(ev, ct);

            // Publica evento de integração via Outbox
            _ = EnqueueIntegrationEventAsync(
                eventType: "GameStarted",
                aggregateId: gameId.ToString(),
                data: new Dictionary<string, object?>
                {
                    ["GameId"] = gameId.ToString(),
                    ["UserId"] = userId.ToString(),
                    ["GameName"] = game.Name ?? ""
                });

            return ResponseModel<bool>.Ok(true);
        }

        public async Task<ResponseModel<bool>> QueueGameAsync(ObjectId gameId, ObjectId userId, CancellationToken ct = default)
        {
            var game = await gameRepository.GetByIdAsync<ProjectGameDTO>(gameId);
            if (game is null)
                return ResponseModel<bool>.NotFound("Jogo não encontrado");

            // Registra evento local
            var ev = DomainEvent.Create(
                aggregateId: gameId,
                type: "GameQueued",
                data: new Dictionary<string, object?>
                {
                    ["GameId"] = gameId.ToString(),
                    ["UserId"] = userId.ToString(),
                    ["GameName"] = game.Name,
                    ["QueuedAt"] = DateTime.UtcNow
                }
            );
            await eventRepo.AppendEventAsync(ev, ct);

            // Publica evento de integração via Outbox
            _ = EnqueueIntegrationEventAsync(
                eventType: "GameQueued",
                aggregateId: gameId.ToString(),
                data: new Dictionary<string, object?>
                {
                    ["GameId"] = gameId.ToString(),
                    ["UserId"] = userId.ToString(),
                    ["GameName"] = game.Name ?? "",
                    ["QueuedAt"] = DateTime.UtcNow.ToString("O")
                });

            return ResponseModel<bool>.Ok(true);
        }

        private async Task EnqueueIntegrationEventAsync(string eventType, string aggregateId, Dictionary<string, object?> data)
        {
            try
            {
                var queueUrl = _configuration["Sqs:GamesEventsQueueUrl"] ?? _configuration["GAMES_EVENTS_QUEUE_URL"];
                if (string.IsNullOrWhiteSpace(queueUrl)) return;

                var message = new GameEventMessage(eventType, gameId, userId, DateTime.UtcNow, data);
                var body = JsonSerializer.Serialize(message);

                var body = JsonSerializer.Serialize(env);
                var outbox = new OutboxMessage
                {
                    EventId = env.EventId,
                    EventType = env.Type,
                    Source = env.Source,
                    AggregateId = env.AggregateId,
                    CorrelationId = env.CorrelationId,
                    CausationId = env.CausationId,
                    Version = env.Version,
                    Destination = queueUrl,
                    Body = body
                };

                await outboxRepository.EnqueueAsync(outbox, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Outbox] Erro ao enfileirar evento {eventType}: {ex.Message}");
            }
        }
    }
}
