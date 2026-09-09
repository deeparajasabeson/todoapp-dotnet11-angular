namespace TodoApi.Models;

/// <summary>Lifecycle state of a to-do item.</summary>
public enum TodoStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3
}
