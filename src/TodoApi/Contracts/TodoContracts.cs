using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using TodoApi.Models;

namespace TodoApi.Contracts;

/// <summary>Payload for creating a to-do item.</summary>
public record CreateTodoRequest(
    [property: Required, MaxLength(200)] string Title,
    [property: MaxLength(2000)] string? Description = null,
    TodoStatus? Status = null,
    TodoPriority? Priority = null,
    DateTimeOffset? DueDate = null);

/// <summary>Full replacement payload for an existing to-do item.</summary>
public record UpdateTodoRequest(
    [property: Required, MaxLength(200)] string Title,
    TodoStatus Status,
    TodoPriority Priority,
    [property: MaxLength(2000)] string? Description = null,
    DateTimeOffset? DueDate = null);

/// <summary>Payload for moving an item to a new status.</summary>
public record UpdateStatusRequest(TodoStatus Status);

/// <summary>Payload for re-prioritising an item.</summary>
public record UpdatePriorityRequest(TodoPriority Priority);

/// <summary>A to-do item as returned by the API.</summary>
public record TodoResponse(
    Guid Id,
    string Title,
    string? Description,
    TodoStatus Status,
    TodoPriority Priority,
    DateTimeOffset? DueDate,
    bool IsOverdue,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt)
{
    public static TodoResponse FromEntity(TodoItem item) => new(
        item.Id,
        item.Title,
        item.Description,
        item.Status,
        item.Priority,
        item.DueDate,
        IsOverdue: item.DueDate is { } due
            && due < DateTimeOffset.UtcNow
            && item.Status is not (TodoStatus.Completed or TodoStatus.Cancelled),
        item.CreatedAt,
        item.UpdatedAt,
        item.CompletedAt);
}

/// <summary>One page of results plus the paging metadata needed to fetch the next.</summary>
public record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

/// <summary>Fields a to-do list can be sorted by.</summary>
public enum TodoSortBy
{
    CreatedAt = 0,
    UpdatedAt = 1,
    DueDate = 2,
    Priority = 3,
    Status = 4,
    Title = 5
}

/// <summary>Filtering, sorting and paging options for <c>GET /api/todos</c>.</summary>
public record TodoQuery
{
    /// <summary>Only return items in this status.</summary>
    public TodoStatus? Status { get; init; }

    /// <summary>Only return items at this exact priority.</summary>
    public TodoPriority? Priority { get; init; }

    /// <summary>Only return items at this priority or higher.</summary>
    public TodoPriority? MinPriority { get; init; }

    /// <summary>Case-insensitive match against title and description.</summary>
    public string? Search { get; init; }

    /// <summary>Only return items whose due date has passed and are not finished.</summary>
    public bool? IsOverdue { get; init; }

    /// <summary>Only return items due on or before this instant.</summary>
    public DateTimeOffset? DueBefore { get; init; }

    // Nullable so [AsParameters] treats them as optional query string values;
    // the defaults below are applied by the handler.
    [DefaultValue(TodoSortBy.CreatedAt)]
    public TodoSortBy? SortBy { get; init; }

    [DefaultValue(false)]
    public bool? Descending { get; init; }

    [Range(1, int.MaxValue)]
    [DefaultValue(DefaultPage)]
    public int? Page { get; init; }

    [Range(1, MaxPageSize)]
    [DefaultValue(DefaultPageSize)]
    public int? PageSize { get; init; }

    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;
}
