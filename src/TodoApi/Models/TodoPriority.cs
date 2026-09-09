namespace TodoApi.Models;

/// <summary>How urgent a to-do item is. Higher value means more urgent.</summary>
public enum TodoPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}
