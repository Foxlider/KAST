using System.Threading.Tasks;

namespace KAST.Desktop.Contracts.Services;

public interface IActivationService
{
    Task ActivateAsync(object activationArgs);
}
