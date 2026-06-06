using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface IServerConsoleLogTailer
{
    void StartFollowing(ServerInstance instance, DateTime sessionStartedUtc, bool replayExistingContent);
    Task StopFollowingAsync(int serverInstanceId);
}
