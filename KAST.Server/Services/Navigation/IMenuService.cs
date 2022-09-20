using KAST.Server.Models.SideMenu;

namespace KAST.Server.Services.Navigation
{
    public interface IMenuService
    {
        IEnumerable<MenuSectionModel> Features { get; }
    }
}