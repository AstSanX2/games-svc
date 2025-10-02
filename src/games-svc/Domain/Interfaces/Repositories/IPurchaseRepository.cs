using Domain.Entities;

namespace Domain.Interfaces.Repositories
{
    public interface IPurchaseRepository
    {
        Task AppendEventAsync(DomainEvent ev, CancellationToken ct);
        Task CreateAsync(Purchase purchase, CancellationToken ct);
    }
}
