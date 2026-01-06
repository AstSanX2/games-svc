using MongoDB.Bson;

namespace Application.DTO.PurchaseDTO
{
    public class CreatePurchaseDTO
    {
        public ObjectId GameId { get; set; } = default!;
    }
}
