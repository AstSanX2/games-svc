using Domain.Entities;
using Domain.Interfaces.Repositories;
using MongoDB.Driver;

namespace Infraestructure.Repositories
{
    public class PurchaseRepository : IPurchaseRepository
    {
        private readonly IMongoCollection<Purchase> _purchases;
        private readonly IMongoCollection<DomainEvent> _events;

        public PurchaseRepository(IMongoDatabase db)
        {
            _purchases = db.GetCollection<Purchase>("purchases");
            _events = db.GetCollection<DomainEvent>("events");
        }

        public Task AppendEventAsync(DomainEvent ev, CancellationToken ct) =>
            _events.InsertOneAsync(ev, cancellationToken: ct);

        public Task CreateAsync(Purchase purchase, CancellationToken ct) =>
            _purchases.InsertOneAsync(purchase, cancellationToken: ct);
    }
}
