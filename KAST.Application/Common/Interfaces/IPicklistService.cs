using KAST.Application.Features.KeyValues.DTOs;

namespace KAST.Application.Common.Interfaces
{
    public interface IPicklistService
    {
        List<KeyValueDto> DataSource { get; }
        event Action? OnChange;
        Task Initialize();
        Task Refresh();
    }
}