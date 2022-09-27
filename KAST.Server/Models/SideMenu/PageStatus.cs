using System.ComponentModel;

namespace KAST.Server.Models.SideMenu
{
    public enum PageStatus
    {
        [Description("Coming Soon")] ComingSoon,
        [Description("WIP")] Wip,
        [Description("New")] New,
        [Description("Completed")] Completed
    }
}