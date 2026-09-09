using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Contracts;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Endpoints;

public static class TodoEndpoints
{
    public static RouteGroupBuilder MapTodoEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/todos")
            .WithTags("Todos");

        group.MapGet("/", ListAsync)
            .WithName("ListTodos")
            .WithSummary("List to-do items with filtering, sorting and paging.");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetTodo")
            .WithSummary("Get a single to-do item by id.");

        group.MapPost("/", CreateAsync)
            .WithName("CreateTodo")
            .WithSummary("Create a to-do item.");

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateTodo")
            .WithSummary("Replace a to-do item.");

        group.MapPatch("/{id:guid}/status", UpdateStatusAsync)
            .WithName("UpdateTodoStatus")
            .WithSummary("Move a to-do item to a new status.");

        group.MapPatch("/{id:guid}/priority", UpdatePriorityAsync)
            .WithName("UpdateTodoPriority")
            .WithSummary("Change the priority of a to-do item.");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteTodo")
            .WithSummary("Delete a to-do item.");

        return group;
    }

    private static async Task<Ok<PagedResponse<TodoResponse>>> ListAsync(
        [AsParameters] TodoQuery query,
        TodoDbContext db,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page ?? TodoQuery.DefaultPage, 1);
        var pageSize = Math.Clamp(query.PageSize ?? TodoQuery.DefaultPageSize, 1, TodoQuery.MaxPageSize);

        IQueryable<TodoItem> items = db.Todos.AsNoTracking();

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
            // ToLower rather than StringComparison so the filter still translates
            // to SQL if this moves onto a relational provider.
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

        var totalCount = await items.CountAsync(cancellationToken);

        var results = await ApplySort(items, query.SortBy ?? TodoSortBy.CreatedAt, query.Descending ?? false)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var response = new PagedResponse<TodoResponse>(
            results.Select(TodoResponse.FromEntity).ToList(),
            page,
            pageSize,
            totalCount);

        return TypedResults.Ok(response);
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

    private static async Task<Results<Ok<TodoResponse>, NotFound<ProblemDetails>>> GetAsync(
        Guid id,
        TodoDbContext db,
        CancellationToken cancellationToken)
    {
        var item = await db.Todos.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        return item is null
            ? TypedResults.NotFound(NotFoundProblem(id))
            : TypedResults.Ok(TodoResponse.FromEntity(item));
    }

    private static async Task<Results<Created<TodoResponse>, ValidationProblem>> CreateAsync(
        CreateTodoRequest request,
        TodoDbContext db,
        CancellationToken cancellationToken)
    {
        if (Validate(request.Title, request.Description, request.Status, request.Priority) is { } problem)
        {
            return problem;
        }

        var now = DateTimeOffset.UtcNow;
        var status = request.Status ?? TodoStatus.Pending;

        var item = new TodoItem
        {
            Title = request.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Status = status,
            Priority = request.Priority ?? TodoPriority.Medium,
            DueDate = request.DueDate,
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = status == TodoStatus.Completed ? now : null
        };

        db.Todos.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/todos/{item.Id}", TodoResponse.FromEntity(item));
    }

    private static async Task<Results<Ok<TodoResponse>, NotFound<ProblemDetails>, ValidationProblem>> UpdateAsync(
        Guid id,
        UpdateTodoRequest request,
        TodoDbContext db,
        CancellationToken cancellationToken)
    {
        if (Validate(request.Title, request.Description, request.Status, request.Priority) is { } problem)
        {
            return problem;
        }

        var item = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound(NotFoundProblem(id));
        }

        item.Title = request.Title.Trim();
        item.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        item.Priority = request.Priority;
        item.DueDate = request.DueDate;
        ApplyStatus(item, request.Status);
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(TodoResponse.FromEntity(item));
    }

    private static async Task<Results<Ok<TodoResponse>, NotFound<ProblemDetails>, ValidationProblem>> UpdateStatusAsync(
        Guid id,
        UpdateStatusRequest request,
        TodoDbContext db,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Status))
        {
            return ValidationProblemFor("status", "Value is not a valid status.");
        }

        var item = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound(NotFoundProblem(id));
        }

        ApplyStatus(item, request.Status);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(TodoResponse.FromEntity(item));
    }

    private static async Task<Results<Ok<TodoResponse>, NotFound<ProblemDetails>, ValidationProblem>> UpdatePriorityAsync(
        Guid id,
        UpdatePriorityRequest request,
        TodoDbContext db,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Priority))
        {
            return ValidationProblemFor("priority", "Value is not a valid priority.");
        }

        var item = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound(NotFoundProblem(id));
        }

        item.Priority = request.Priority;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(TodoResponse.FromEntity(item));
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>>> DeleteAsync(
        Guid id,
        TodoDbContext db,
        CancellationToken cancellationToken)
    {
        var item = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound(NotFoundProblem(id));
        }

        db.Todos.Remove(item);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
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

    private static ValidationProblem? Validate(
        string? title,
        string? description,
        TodoStatus? status,
        TodoPriority? priority)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(title))
        {
            errors["title"] = ["Title is required."];
        }
        else if (title.Trim().Length > 200)
        {
            errors["title"] = ["Title must be 200 characters or fewer."];
        }

        if (description is { Length: > 2000 })
        {
            errors["description"] = ["Description must be 2000 characters or fewer."];
        }

        if (status is { } s && !Enum.IsDefined(s))
        {
            errors["status"] = ["Value is not a valid status."];
        }

        if (priority is { } p && !Enum.IsDefined(p))
        {
            errors["priority"] = ["Value is not a valid priority."];
        }

        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static ValidationProblem ValidationProblemFor(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [message]
        });

    private static ProblemDetails NotFoundProblem(Guid id) => new()
    {
        Title = "To-do item not found",
        Detail = $"No to-do item exists with id '{id}'.",
        Status = StatusCodes.Status404NotFound
    };
}
