using Application.DTO.GameDTO;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Infraestructure.Repositories
{
    public class PurchaseRepository : IPurchaseRepository
    {
        private readonly IMongoCollection<Purchase> _purchases;

        public PurchaseRepository(IMongoDatabase db)
        {
            _purchases = db.GetCollection<Purchase>("Purchases");

            // Idempotência simples: não permitir múltiplas compras para o mesmo (UserId, GameId)
            // (bloqueia tanto PENDING quanto PAID, conforme requisito)
            try
            {
                var keys = Builders<Purchase>.IndexKeys
                    .Ascending(x => x.UserId)
                    .Ascending(x => x.GameId);

                var options = new CreateIndexOptions
                {
                    Unique = true,
                    Name = "ux_purchases_user_game"
                };

                _purchases.Indexes.CreateOne(new CreateIndexModel<Purchase>(keys, options));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Mongo] Falha ao garantir índice ux_purchases_user_game: {ex.Message}");
            }
        }

        public Task CreateAsync(Purchase purchase, CancellationToken ct) =>
            _purchases.InsertOneAsync(purchase, cancellationToken: ct);

        public Task<bool> ExistsActiveAsync(ObjectId userId, ObjectId gameId, CancellationToken ct)
        {
            var filter = Builders<Purchase>.Filter.And(
                Builders<Purchase>.Filter.Eq(x => x.UserId, userId),
                Builders<Purchase>.Filter.Eq(x => x.GameId, gameId),
                Builders<Purchase>.Filter.In(x => x.Status, new[] { "PENDING", "PAID" })
            );

            return _purchases.Find(filter).AnyAsync(ct);
        }

        public async Task<List<ObjectId>> GetUserPaidGameIdsAsync(ObjectId userId, int max = 10)
        {
            var pipeline = new[]
            {
                new BsonDocument("$match", new BsonDocument {
                    { "UserId", userId },
                    { "Status", "PAID" }
                }),
                new BsonDocument("$sort", new BsonDocument { { "CreatedAt", -1 } }),
                new BsonDocument("$limit", max),
                new BsonDocument("$project", new BsonDocument { { "_id", 0 }, { "GameId", 1 } })
            };

            var docs = await _purchases.Aggregate<BsonDocument>(pipeline).ToListAsync();
            return docs.Select(d => d["GameId"].AsObjectId).ToList();
        }

        public async Task<List<ProjectGameDTO>> GetUserPaidGamesAsync(ObjectId userId, int max = 100)
        {
            if (max < 1) max = 1;
            if (max > 500) max = 500;

            var pipeline = new[]
            {
                new BsonDocument("$match", new BsonDocument {
                    { "UserId", userId },
                    { "Status", "PAID" }
                }),
                new BsonDocument("$sort", new BsonDocument { { "CreatedAt", -1 } }),
                new BsonDocument("$limit", max ),
                new BsonDocument("$lookup", new BsonDocument {
                    { "from", "Game" },
                    { "localField", "GameId" },
                    { "foreignField", "_id" },
                    { "as", "Game" }
                }),
                new BsonDocument("$unwind", "$Game"),
                new BsonDocument("$replaceRoot", new BsonDocument("newRoot", "$Game")),
                new BsonDocument("$project", new BsonDocument {
                    { "_id", 1 }, { "Name", 1 }, { "Description", 1 }, { "Category", 1 }, { "Price", 1 },
                    { "ReleaseDate", 1 }, { "LastUpdateDate", 1 }
                })
            };

            return await _purchases.Aggregate<ProjectGameDTO>(pipeline).ToListAsync();
        }

        public async Task<List<ProjectGameDTO>> GetTopPopularAsync(int limit = 10)
        {
            // Agrupa compras pagas por gameId, ordena, limita e dá lookup em Game
            var pipeline = new[]
            {
                new BsonDocument("$match", new BsonDocument { { "Status", "PAID" } }),
                new BsonDocument("$group", new BsonDocument {
                    { "_id", "$GameId" },
                    { "Total", new BsonDocument("$sum", 1) }
                }),
                new BsonDocument("$sort", new BsonDocument { { "Total", -1 } }),
                new BsonDocument("$limit", limit ),
                new BsonDocument("$lookup", new BsonDocument {
                    { "from", "Game" },
                    { "localField", "_id" },
                    { "foreignField", "_id" },
                    { "as", "Game" }
                }),
                new BsonDocument("$unwind", "$Game"),
                new BsonDocument("$replaceRoot", new BsonDocument("newRoot", "$Game")),
                new BsonDocument("$project", new BsonDocument {
                    { "_id", 1 }, { "Name", 1 }, { "Description", 1 }, { "Category", 1 }, { "Price", 1 }
                })
            };

            var result = await _purchases.Aggregate<ProjectGameDTO>(pipeline).ToListAsync();
            return result;
        }
    }
}
