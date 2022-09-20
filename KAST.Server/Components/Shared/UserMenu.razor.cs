using KAST.Server.Components.Dialogs;
using KAST.Application.Common.Models;
using KAST.Application.Constants;
using KAST.Infrastructure.Services.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;

namespace KAST.Server.Components.Shared
{
    public partial class UserMenu
    {

        [EditorRequired][Parameter] public UserProfile UserProfile { get; set; } = default!;
        [Parameter] public EventCallback<MouseEventArgs> OnSettingClick { get; set; }
        [Inject] private IdentityAuthenticationService _authenticationService { get; set; } = default!;
        [Inject] private IJSRuntime JS { get; set; } = default!;
        private async Task OnLogout()
        {
            var parameters = new DialogParameters
            {
                { nameof(LogoutConfirmation.ContentText), $"{PromptText.LOGOUTCONFIRMATION}"},
                { nameof(LogoutConfirmation.Color), Color.Error}
            };

            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };
            var dialog = DialogService.Show<LogoutConfirmation>(PromptText.LOGOUTCONFIRMATIONTITLE, parameters, options);
            var result = await dialog.Result;
            if (!result.Cancelled)
            {
                await _authenticationService.Logout();
                await JS.InvokeVoidAsync("externalLogout");
            }
        }
    }
}