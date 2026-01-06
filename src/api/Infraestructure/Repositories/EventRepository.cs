using Domain.Entities;
using Domain.Interfaces.Repositories;
using MongoDB.Driver;

namespace Infraestructure.Repositories
{
    public class EventRepository(IMongoDatabase db) : IEventRepository
    {
        private readonly IMongoCollection<DomainEvent> _events = db.GetCollection<DomainEvent>("Events");

        public Task AppendEventAsync(DomainEvent ev, CancellationToken ct) =>
            _events.InsertOneAsync(ev, cancellationToken: ct);
    }
}
