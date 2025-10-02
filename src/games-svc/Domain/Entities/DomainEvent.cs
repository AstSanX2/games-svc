using MongoDB.Bson;

namespace Domain.Entities
{
    // Evento genérico para event sourcing
    public class DomainEvent : BaseEntity
    {
        public ObjectId AggregateId { get; set; } = default!;
        public string Type { get; set; } = default!;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int Seq { get; set; } = 1;

        public BsonDocument Data { get; set; } = new();

        public static DomainEvent Create(ObjectId aggregateId, string type, IDictionary<string, object?> data, int seq = 1)
        {
            return new DomainEvent
            {
                AggregateId = aggregateId,
                Type = type,
                Timestamp = DateTime.UtcNow,
                Seq = seq,
                Data = new BsonDocument(data.Select(kv => new BsonElement(kv.Key, kv.Value is null ? BsonNull.Value : BsonValue.Create(kv.Value))))
            };
        }
    }
}
