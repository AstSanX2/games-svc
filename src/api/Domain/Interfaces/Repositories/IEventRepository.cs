using Domain.Entities;

namespace Domain.Interfaces.Repositories
{
    public interface IEventRepository
    {
        Task AppendEventAsync(DomainEvent ev, CancellationToken ct);
    }
}
