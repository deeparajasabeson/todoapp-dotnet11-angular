using System.ComponentModel.DataAnnotations;

namespace TodoApi.Models;

/// <summary>A single to-do item.</summary>
public class TodoItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public TodoStatus Status { get; set; } = TodoStatus.Pending;

    public TodoPriority Priority { get; set; } = TodoPriority.Medium;

    public DateTimeOffset? DueDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set the first time the item moves to <see cref="TodoStatus.Completed"/>.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
