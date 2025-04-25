using KAST.Core.Helpers;
using KAST.Data;
using KAST.Data.Models;

namespace KAST.Core.Services
{
    public class InstanceManagerService : TracedServiceBase
    {
        public List<ServerInstance> Servers { get; } = new();

        private readonly ApplicationDbContext _dbContext;

        public InstanceManagerService(ITracingNamingProvider namingProvider, ApplicationDbContext context) : base(namingProvider)
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
                server.PerfCfgService.SaveFile();
            }
        }
    }

    public class ServerInstance
    {
        public Server Server { get; set; }

        public ConfigFileService<PerfConf> PerfCfgService { get; set; }
        public PerfConf PerfCfg => PerfCfgService.Config;

        public ConfigFileService<ServerConfig> ServerCfgService { get; set; }
        public ServerConfig ServerCfg => ServerCfgService.Config;

        public ConfigFileService<ServerProfile> ServerProfileService { get; set; }
        public ServerProfile ServerProfile => ServerProfileService.Config;

        public ServerInstance(string name)
        {
            
            var id = GuidHelper.NewGuid(name);
            Server = new Server
            {
                Id = id,
                Name = name,
                InstallPath = Path.Combine(Directory.GetCurrentDirectory(), id.ToString()) // TODO Use Server Default Install path from Settings later
            };
            PerfCfgService = new ConfigFileService<PerfConf>(
                Path.Combine(Server.InstallPath, PerfConf.FILENAME),
                new KeyValueConfigFormat<PerfConf>()
            );

            ServerCfgService = new ConfigFileService<ServerConfig>(
                Path.Combine(Server.InstallPath, ServerConfig.FILENAME),
                new KeyValueConfigFormat<ServerConfig>()
            );

            ServerProfileService = new ConfigFileService<ServerProfile>(
                Path.Combine(Server.InstallPath, ServerProfile.FILENAME),
                new ClassHierarchyConfigFormat<ServerProfile>()
            );
        }

        public ServerInstance(Server server)
        {
            Server = server;
            PerfCfgService = new ConfigFileService<PerfConf>(
                Path.Combine(Server.InstallPath, PerfConf.FILENAME),
                new KeyValueConfigFormat<PerfConf>()
            );

            ServerCfgService = new ConfigFileService<ServerConfig>(
                Path.Combine(Server.InstallPath, ServerConfig.FILENAME),
                new KeyValueConfigFormat<ServerConfig>()
            );

            ServerProfileService = new ConfigFileService<ServerProfile>(
                Path.Combine(Server.InstallPath, ServerProfile.FILENAME),
                new ClassHierarchyConfigFormat<ServerProfile>()
            );
        }
    }
}
