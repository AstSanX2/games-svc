using Domain.Models.Validation;

namespace Application.DTO.Bases.Interfaces
{
    public interface IValidator
    {
        ValidationResultModel Validate();
    }
}
