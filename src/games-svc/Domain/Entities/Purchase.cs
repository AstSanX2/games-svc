using MongoDB.Bson;

namespace Domain.Entities
{
    public class Purchase : BaseEntity
    {
        public ObjectId GameId { get; set; }
        public ObjectId UserId { get; set; } = default!;
        public decimal Amount { get; set; }
        public string Status { get; set; } = "PENDING";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
