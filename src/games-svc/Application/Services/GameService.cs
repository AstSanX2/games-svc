using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Application.DTO.GameDTO;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using Domain.Models.Response;
using MongoDB.Bson;
using System.Text.Json;

namespace Application.Services
{
    // Mensagens de eventos para SQS
    public record GameEventMessage(string EventType, string GameId, string UserId, DateTime Timestamp, Dictionary<string, object>? Data = null);

    public class GameService(
        IGameRepository gameRepository,
        IPurchaseRepository purchaseRepository,
        IEventRepository eventRepo,
        IConfiguration configuration,
        IHostEnvironment env) : IGameService
    {
        private readonly IAmazonSQS _sqs = CreateSqsClient(configuration);
        private readonly IConfiguration _configuration = configuration;
        private readonly IHostEnvironment _env = env;

        private static IAmazonSQS CreateSqsClient(IConfiguration configuration)
        {
            var serviceUrl = configuration["Sqs:ServiceUrl"] ?? Environment.GetEnvironmentVariable("SQS_SERVICE_URL");
            if (!string.IsNullOrEmpty(serviceUrl))
            {
                // LocalStack ou outro emulador
                var config = new AmazonSQSConfig { ServiceURL = serviceUrl };
                var accessKey = configuration["AWS:AccessKey"];
                var secretKey = configuration["AWS:SecretKey"];
                if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey))
                    return new AmazonSQSClient(new BasicAWSCredentials(accessKey, secretKey), config);

                return new AmazonSQSClient(new BasicAWSCredentials("test", "test"), config);
            }
            // AWS real (credenciais via appsettings ou cadeia default)
            var region = configuration["AWS:Region"] ?? Environment.GetEnvironmentVariable("AWS_REGION");
            var sqsConfig = new AmazonSQSConfig();
            if (!string.IsNullOrWhiteSpace(region))
                sqsConfig.RegionEndpoint = RegionEndpoint.GetBySystemName(region);

            var ak = configuration["AWS:AccessKey"];
            var sk = configuration["AWS:SecretKey"];
            if (!string.IsNullOrWhiteSpace(ak) && !string.IsNullOrWhiteSpace(sk))
                return new AmazonSQSClient(new BasicAWSCredentials(ak, sk), sqsConfig);

            return new AmazonSQSClient(sqsConfig);
        }
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

            // Publica na SQS (fire-and-forget)
            _ = PublishGameEventAsync("GameStarted", gameId.ToString(), userId.ToString(), new Dictionary<string, object>
            {
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

            // Publica na SQS (fire-and-forget)
            _ = PublishGameEventAsync("GameQueued", gameId.ToString(), userId.ToString(), new Dictionary<string, object>
            {
                ["GameName"] = game.Name ?? "",
                ["QueuedAt"] = DateTime.UtcNow.ToString("O")
            });

            return ResponseModel<bool>.Ok(true);
        }

        private async Task PublishGameEventAsync(string eventType, string gameId, string userId, Dictionary<string, object>? data = null)
        {
            try
            {
                var queueUrl = GetQueueUrl();
                if (string.IsNullOrEmpty(queueUrl))
                {
                    Console.WriteLine($"[SQS] Evento {eventType} para game {gameId} (SQS não configurado)");
                    return;
                }

                var message = new GameEventMessage(eventType, gameId, userId, DateTime.UtcNow, data);
                var body = JsonSerializer.Serialize(message);

                await _sqs.SendMessageAsync(new SendMessageRequest
                {
                    QueueUrl = queueUrl,
                    MessageBody = body
                });

                Console.WriteLine($"[SQS] Evento {eventType} publicado para game {gameId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SQS] Erro ao publicar evento {eventType}: {ex.Message}");
            }
        }

        private string? GetQueueUrl()
        {
            // 1) env var
            var queueUrl = Environment.GetEnvironmentVariable("GAMES_EVENTS_QUEUE_URL");
            if (!string.IsNullOrEmpty(queueUrl)) return queueUrl;

            // 2) appsettings (K8s: arquivo montado; Local: arquivo do repo)
            return _configuration["Sqs:GamesEventsQueueUrl"]
                ?? _configuration["GAMES_EVENTS_QUEUE_URL"];
        }
    }
}
