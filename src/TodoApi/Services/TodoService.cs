using Microsoft.EntityFrameworkCore;
using TodoApi.Contracts;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Services;

/// <summary>
/// The one implementation of every to-do operation. Both the HTTP endpoints and the chat
/// agent's tools go through here, so an item created by an agent is indistinguishable from
/// one created over REST - same defaults, same <see cref="TodoItem.CompletedAt"/> handling.
/// </summary>
public sealed class TodoService(TodoDbContext db)
{
    public async Task<PagedResponse<TodoResponse>> ListAsync(
        TodoQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page ?? TodoQuery.DefaultPage, 1);
        var pageSize = Math.Clamp(query.PageSize ?? TodoQuery.DefaultPageSize, 1, TodoQuery.MaxPageSize);

        var items = Filter(db.Todos.AsNoTracking(), query);

        var totalCount = await items.CountAsync(cancellationToken);

        var results = await ApplySort(items, query.SortBy ?? TodoSortBy.CreatedAt, query.Descending ?? false)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<TodoResponse>(
            results.Select(TodoResponse.FromEntity).ToList(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<TodoResponse?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.Todos.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return item is null ? null : TodoResponse.FromEntity(item);
    }

    public async Task<TodoResponse> CreateAsync(
        CreateTodoRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var status = request.Status ?? TodoStatus.Pending;

        var item = new TodoItem
        {
            Title = request.Title.Trim(),
            Description = Normalize(request.Description),
            Status = status,
            Priority = request.Priority ?? TodoPriority.Medium,
            DueDate = request.DueDate,
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = status == TodoStatus.Completed ? now : null
        };

        db.Todos.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return TodoResponse.FromEntity(item);
    }

    public async Task<TodoResponse?> ReplaceAsync(
        Guid id,
        UpdateTodoRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (item is null)
        {
            return null;
        }

        item.Title = request.Title.Trim();
        item.Description = Normalize(request.Description);
        item.Priority = request.Priority;
        item.DueDate = request.DueDate;
        ApplyStatus(item, request.Status);
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return TodoResponse.FromEntity(item);
    }

    public async Task<TodoResponse?> SetStatusAsync(
        Guid id,
        TodoStatus status,
        CancellationToken cancellationToken = default)
    {
        var item = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (item is null)
        {
            return null;
        }

        ApplyStatus(item, status);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return TodoResponse.FromEntity(item);
    }

    public async Task<TodoResponse?> SetPriorityAsync(
        Guid id,
        TodoPriority priority,
        CancellationToken cancellationToken = default)
    {
        var item = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (item is null)
        {
            return null;
        }

        item.Priority = priority;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return TodoResponse.FromEntity(item);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (item is null)
        {
            return false;
        }

        db.Todos.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Counts by status and priority, for "how am I doing?" style questions.</summary>
    public async Task<TodoStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var byStatus = await db.Todos.AsNoTracking()
            .GroupBy(t => t.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byPriority = await db.Todos.AsNoTracking()
            .GroupBy(t => t.Priority)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var overdue = await db.Todos.AsNoTracking().CountAsync(
            t => t.DueDate != null && t.DueDate < now
                && t.Status != TodoStatus.Completed && t.Status != TodoStatus.Cancelled,
            cancellationToken);

        return new TodoStats(
            byStatus.Sum(x => x.Count),
            byStatus.ToDictionary(x => x.Key.ToString(), x => x.Count),
            byPriority.ToDictionary(x => x.Key.ToString(), x => x.Count),
            overdue);
    }

    private static string? Normalize(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static IQueryable<TodoItem> Filter(IQueryable<TodoItem> items, TodoQuery query)
    {
        if (query.Status is { } status)
        {
            items = items.Where(t => t.Status == status);
        }

        if (query.Priority is { } priority)
        {
            items = items.Where(t => t.Priority == priority);
        }

        if (query.MinPriority is { } minPriority)
        {
            items = items.Where(t => t.Priority >= minPriority);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // ToLower rather than StringComparison so the filter still translates to SQL.
            var term = query.Search.Trim().ToLowerInvariant();
            items = items.Where(t =>
                t.Title.ToLower().Contains(term)
                || (t.Description != null && t.Description.ToLower().Contains(term)));
        }

        if (query.DueBefore is { } dueBefore)
        {
            items = items.Where(t => t.DueDate != null && t.DueDate <= dueBefore);
        }

        if (query.IsOverdue is { } isOverdue)
        {
            var now = DateTimeOffset.UtcNow;
            items = isOverdue
                ? items.Where(t => t.DueDate != null && t.DueDate < now
                    && t.Status != TodoStatus.Completed && t.Status != TodoStatus.Cancelled)
                : items.Where(t => t.DueDate == null || t.DueDate >= now
                    || t.Status == TodoStatus.Completed || t.Status == TodoStatus.Cancelled);
        }

        return items;
    }

    private static IQueryable<TodoItem> ApplySort(IQueryable<TodoItem> items, TodoSortBy sortBy, bool descending)
    {
        IOrderedQueryable<TodoItem> ordered = sortBy switch
        {
            TodoSortBy.UpdatedAt => descending
                ? items.OrderByDescending(t => t.UpdatedAt)
                : items.OrderBy(t => t.UpdatedAt),
            // Items with no due date sort last either way - an open-ended item is
            // never the most urgent thing on the list.
            TodoSortBy.DueDate => descending
                ? items.OrderBy(t => t.DueDate == null).ThenByDescending(t => t.DueDate)
                : items.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate),
            TodoSortBy.Priority => descending
                ? items.OrderByDescending(t => t.Priority)
                : items.OrderBy(t => t.Priority),
            TodoSortBy.Status => descending
                ? items.OrderByDescending(t => t.Status)
                : items.OrderBy(t => t.Status),
            TodoSortBy.Title => descending
                ? items.OrderByDescending(t => t.Title)
                : items.OrderBy(t => t.Title),
            _ => descending
                ? items.OrderByDescending(t => t.CreatedAt)
                : items.OrderBy(t => t.CreatedAt)
        };

        // Ties are broken by id so paging stays stable across requests.
        return ordered.ThenBy(t => t.Id);
    }

    /// <summary>
    /// Keeps <see cref="TodoItem.CompletedAt"/> in step with the status: stamped when the
    /// item first completes, cleared when it is re-opened.
    /// </summary>
    private static void ApplyStatus(TodoItem item, TodoStatus status)
    {
        if (status == TodoStatus.Completed)
        {
            item.CompletedAt ??= DateTimeOffset.UtcNow;
        }
        else
        {
            item.CompletedAt = null;
        }

        item.Status = status;
    }
}

public record TodoStats(
    int Total,
    IReadOnlyDictionary<string, int> ByStatus,
    IReadOnlyDictionary<string, int> ByPriority,
    int Overdue);
