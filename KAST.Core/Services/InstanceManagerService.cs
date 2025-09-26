using KAST.Core.Helpers;
using KAST.Data;
using KAST.Data.Models;
using Microsoft.Extensions.Logging;

namespace KAST.Core.Services
{
    public class InstanceManagerService : TracedServiceBase
    {
        public List<ServerInstance> Servers { get; } = new();

        private readonly ApplicationDbContext _dbContext;

        public InstanceManagerService(ITracingNamingProvider namingProvider, ApplicationDbContext context, 
            ILogger<InstanceManagerService> logger) : base(namingProvider, logger)
        {
            _dbContext = context;
            
            ExecuteWithTelemetry<int>("InstanceManagerService.LoadServers", (activity) =>
            {
                var servers = _dbContext.Servers.ToList();
                activity?.SetTag("server.count", servers.Count);
                
                foreach (var server in servers)
                {
                    Servers.Add(new ServerInstance(server));
                }
                
                Logger.LogInformation("Loaded {ServerCount} servers", servers.Count);
                
                return servers.Count;
            });
        }

        public void AddServer(string name)
        {
            ExecuteWithTelemetry<object?>("InstanceManagerService.AddServer", (activity) =>
            {
                activity?.SetTag("server.name", name);
                
                ServerInstance s = new ServerInstance(name);
                Servers.Add(s);
                _dbContext.Servers.Add(s.Server);
                
                Logger.LogInformation("Added new server: {ServerName} with ID: {ServerId}", name, s.Server.Id);
                return null;
            }, new[] { new KeyValuePair<string, object?>("server.name", name) });
        }

        public void SaveAll()
        {
            ExecuteWithTelemetry<object?>("InstanceManagerService.SaveAll", (activity) =>
            {
                activity?.SetTag("server.count", Servers.Count);
                
                var savedCount = 0;
                foreach (var server in Servers)
                {
                    try
                    {
                        server.PerfCfgService.SaveFile();
                        savedCount++;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Failed to save configuration for server {ServerId}", server.Server.Id);
                    }
                }
                
                activity?.SetTag("server.savedCount", savedCount);
                Logger.LogInformation("Saved configurations for {SavedCount}/{TotalCount} servers", savedCount, Servers.Count);
                return null;
            });
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
                new Arma3ConfigFormat<PerfConf>()
            );

            ServerCfgService = new ConfigFileService<ServerConfig>(
                Path.Combine(Server.InstallPath, ServerConfig.FILENAME),
                new Arma3ConfigFormat<ServerConfig>()
            );

            ServerProfileService = new ConfigFileService<ServerProfile>(
                Path.Combine(Server.InstallPath, ServerProfile.FILENAME),
                new Arma3ConfigFormat<ServerProfile>()
            );
        }

        public ServerInstance(Server server)
        {
            Server = server;
            PerfCfgService = new ConfigFileService<PerfConf>(
                Path.Combine(Server.InstallPath, PerfConf.FILENAME),
                new Arma3ConfigFormat<PerfConf>()
            );

            ServerCfgService = new ConfigFileService<ServerConfig>(
                Path.Combine(Server.InstallPath, ServerConfig.FILENAME),
                new Arma3ConfigFormat<ServerConfig>()
            );

            ServerProfileService = new ConfigFileService<ServerProfile>(
                Path.Combine(Server.InstallPath, ServerProfile.FILENAME),
                new Arma3ConfigFormat<ServerProfile>()
            );
        }
    }
}
