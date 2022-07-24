using KAST.Desktop.Contracts.Services;
using KAST.Desktop.ViewModels;

using Microsoft.UI.Xaml;

using System;
using System.Threading.Tasks;

namespace KAST.Desktop.Activation;

public class DefaultActivationHandler : ActivationHandler<LaunchActivatedEventArgs>
{
    private readonly INavigationService _navigationService;

    public DefaultActivationHandler(INavigationService navigationService)
    { _navigationService = navigationService; }

    protected override bool CanHandleInternal(LaunchActivatedEventArgs args)
    {
        // None of the ActivationHandlers has handled the activation.
        return _navigationService.Frame.Content == null;
    }

    protected override async Task HandleInternalAsync(LaunchActivatedEventArgs args)
    {
        _navigationService.NavigateTo(typeof(ConsoleViewModel).FullName, args.Arguments);

        await Task.CompletedTask;
    }
}
