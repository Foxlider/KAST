using KAST.Core.Helpers;
using KAST.Data;
using KAST.Data.Models;

namespace KAST.Core.Services
{
    public class InstanceManagerService
    {
        public List<ServerInstance> Servers { get; } = new();

        private readonly ApplicationDbContext _dbContext;

        public InstanceManagerService(ApplicationDbContext context)
        {
            _dbContext = context;
            Servers.AddRange(_dbContext.Servers.Select(s => new ServerInstance(s)));
        }

        public void AddServer(string name)
        {
            ServerInstance s = new ServerInstance(name);
            Servers.Add(s);
            _dbContext.Servers.Add(s.Server);
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
        public BasicConfig BasicCfg => BasicCfgService.Settings;

        public ServerInstance(string name)
        {
            var id = GuidHelper.NewGuid(name);
            Server = new Server
            {
                Id = id,
                Name = name,
                InstallPath = Path.Combine(Directory.GetCurrentDirectory(), id.ToString()) // TODO Use Server Default Install path from Settings later
            };
            BasicCfgService = new BasicCfgFileService(Path.Combine(Server.InstallPath, BasicConfig.FILENAME));
        }

        public ServerInstance(Server server)
        {
            Server = server;
            BasicCfgService = new BasicCfgFileService(Path.Combine(Server.InstallPath, BasicConfig.FILENAME));
        }
    }
}
