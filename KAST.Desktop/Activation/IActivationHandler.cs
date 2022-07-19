using System.Threading.Tasks;

namespace KAST.Desktop.Activation;

public interface IActivationHandler
{
    bool CanHandle(object args);

    Task HandleAsync(object args);
}
