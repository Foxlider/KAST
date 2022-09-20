using KAST.Server.Models.SideMenu;
using KAST.Server.Services;
using KAST.Server.Services.Navigation;
using KAST.Application.Common.Models;
using KAST.Infrastructure.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace KAST.Server.Components.Shared
{
    public partial class SideMenu : IDisposable
    {
        private IEnumerable<MenuSectionModel> _menuSections = new List<MenuSectionModel>();

        [EditorRequired]
        [Parameter]
        public bool SideMenuDrawerOpen { get; set; }

        [EditorRequired]
        [Parameter]
        public EventCallback<bool> SideMenuDrawerOpenChanged { get; set; }

        [EditorRequired]
        [Parameter]
        public UserProfile UserProfile { get; set; } = default!;

        [Inject]
        private IMenuService _menuService { get; set; } = default!;

        [CascadingParameter]
        protected Task<AuthenticationState> _authState { get; set; } = default!;

        [Inject]
        private AuthenticationStateProvider _authenticationStateProvider { get; set; } = default!;

        [Inject]
        private LayoutService LayoutService { get; set; } = default!;

        private string[] _roles = new string[] { };

        protected override async Task OnInitializedAsync()
        {
            var authstate = await _authState;
            _roles = authstate.User.GetRoles();
            _menuSections = _menuService.Features;
            _authenticationStateProvider.AuthenticationStateChanged += _authenticationStateProvider_AuthenticationStateChanged;

        }
        private async void _authenticationStateProvider_AuthenticationStateChanged(Task<AuthenticationState> task)
        {
            var authstate = await task;
            _roles = authstate.User.GetRoles();
        }
        public void Dispose()
        {
            _authenticationStateProvider.AuthenticationStateChanged -= _authenticationStateProvider_AuthenticationStateChanged;
        }
    }
}