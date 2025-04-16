using KAST.Core.Helpers;
using KAST.Data.Models;

namespace KAST.Core.Services
{
    public class InstanceManagerService
    {
        public List<ServerInstance> Servers { get; } = new();

        public void AddServer(string name)
        {
            var guid = GuidHelper.NewGuid(name);
            string cfgFilePath = Path.Combine(guid.ToString(), "Basic.cfg");
            var cfgFile = new BasicCfgFileService(cfgFilePath);
            Servers.Add(new ServerInstance
            {
                Server = new Server
                {
                    Id = guid,
                    Name = name,
                    InstallPath = cfgFilePath
                },
                BasicCfgService = new BasicCfgFileService(cfgFilePath)
            });
        }

        public void SaveAll()
        {
            foreach (var server in Servers)
            {
                server.BasicCfgService.UpdateFileFromSettings();
            }
        }
    }

    public class ServerInstance
    {
        public Server Server { get; set; }
        public BasicCfgFileService BasicCfgService { get; set; }
        public CfgSettings BasicCfg => BasicCfgService.Settings;
    }
}
