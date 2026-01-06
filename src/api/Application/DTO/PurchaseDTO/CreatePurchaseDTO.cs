using MongoDB.Bson;

namespace Application.DTO.PurchaseDTO
{
    public class CreatePurchaseDTO
    {
        public ObjectId GameId { get; set; } = default!;
        public decimal Amount { get; set; }
        public ObjectId UserId { get; set; } = default!;
    }
}
