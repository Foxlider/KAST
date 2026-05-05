namespace KAST.Core.Enums;

/// <summary>
/// Status of an individual step within a content install operation.
/// </summary>
public enum ContentStepStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped
}
