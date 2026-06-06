using KAST.Core.Events;

namespace KAST.Core.Interfaces;

public interface IArmaRptEventDetector
{
    IReadOnlyList<ServerRuntimeEvent> Detect(int serverInstanceId, string line, DateTime timestamp);
}
