using Application.DTO.GameDTO;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Infraestructure.Repositories
{
    public class PurchaseRepository(IMongoDatabase db) : IPurchaseRepository
    {
        private readonly IMongoCollection<Purchase> _purchases = db.GetCollection<Purchase>("Purchases");
        private readonly IMongoCollection<DomainEvent> _events = db.GetCollection<DomainEvent>("Events");

        public Task AppendEventAsync(DomainEvent ev, CancellationToken ct) =>
            _events.InsertOneAsync(ev, cancellationToken: ct);

        public Task CreateAsync(Purchase purchase, CancellationToken ct) =>
            _purchases.InsertOneAsync(purchase, cancellationToken: ct);

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
