using MongoDB.Bson;

namespace Domain.Entities
{
    public class BaseEntity
    {
        public ObjectId _id { get; set; }
    }
}
