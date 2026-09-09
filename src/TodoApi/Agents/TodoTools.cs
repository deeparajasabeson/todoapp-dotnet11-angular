using System.ComponentModel;
using System.Text.Json;
using TodoApi.Contracts;
using TodoApi.Models;
using TodoApi.Services;

namespace TodoApi.Agents;

/// <summary>
/// The functions the agents can call. Every one of them goes through
/// <see cref="TodoService"/>, so the chat cannot bypass the rules the REST API enforces.
/// Tools return JSON strings because that is what a model reads most reliably.
/// </summary>
public sealed class TodoTools(TodoService todos)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Ids of items this conversation turn changed. The chat endpoint returns them so the
    /// UI knows to refresh its list instead of polling.
    /// </summary>
    public List<string> ChangedItemIds { get; } = [];

    public bool MadeChanges => ChangedItemIds.Count > 0;

    [Description("List or search to-do items. Returns the matching items with their ids, status, priority and due dates.")]
    public async Task<string> ListTodos(
        [Description("Only this status: Pending, InProgress, Completed or Cancelled.")] string? status = null,
        [Description("Only this priority or higher: Low, Medium, High or Critical.")] string? minPriority = null,
        [Description("Free text matched against title and description.")] string? search = null,
        [Description("True to return only items that are past due and not finished.")] bool? overdueOnly = null,
        [Description("Sort field: CreatedAt, UpdatedAt, DueDate, Priority, Status or Title.")] string? sortBy = null,
        [Description("True for descending order.")] bool? descending = null,
        [Description("Maximum number of items to return. Defaults to 20.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var query = new TodoQuery
        {
            Status = ParseEnum<TodoStatus>(status),
            MinPriority = ParseEnum<TodoPriority>(minPriority),
            Search = search,
            IsOverdue = overdueOnly,
            SortBy = ParseEnum<TodoSortBy>(sortBy) ?? TodoSortBy.Priority,
            Descending = descending ?? true,
            PageSize = Math.Clamp(limit ?? 20, 1, 50)
        };

        var page = await todos.ListAsync(query, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            totalMatching = page.TotalCount,
            returned = page.Items.Count,
            items = page.Items.Select(Summarize)
        }, Json);
    }

    [Description("Get counts of to-do items by status and priority, plus how many are overdue.")]
    public async Task<string> GetStatistics(CancellationToken cancellationToken = default) =>
        JsonSerializer.Serialize(await todos.GetStatsAsync(cancellationToken), Json);

    [Description("Create a new to-do item. Only the title is required.")]
    public async Task<string> CreateTodo(
        [Description("Short title for the item.")] string title,
        [Description("Optional longer notes.")] string? description = null,
        [Description("Low, Medium, High or Critical. Defaults to Medium.")] string? priority = null,
        [Description("Pending, InProgress, Completed or Cancelled. Defaults to Pending.")] string? status = null,
        [Description("Due date as an ISO 8601 instant, e.g. 2026-10-01T17:00:00Z.")] string? dueDate = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error("A title is required to create an item.");
        }

        var created = await todos.CreateAsync(
            new CreateTodoRequest(
                title.Trim(),
                description,
                ParseEnum<TodoStatus>(status),
                ParseEnum<TodoPriority>(priority),
                ParseDate(dueDate)),
            cancellationToken);

        ChangedItemIds.Add(created.Id.ToString());
        return JsonSerializer.Serialize(new { created = Summarize(created) }, Json);
    }

    [Description("Change the status of an existing item. Accepts the item's id or its exact title.")]
    public async Task<string> SetStatus(
        [Description("The item's id, or its title if the id is unknown.")] string idOrTitle,
        [Description("Pending, InProgress, Completed or Cancelled.")] string status,
        CancellationToken cancellationToken = default)
    {
        if (ParseEnum<TodoStatus>(status) is not { } parsed)
        {
            return Error($"'{status}' is not a valid status. Use Pending, InProgress, Completed or Cancelled.");
        }

        var resolved = await ResolveAsync(idOrTitle, cancellationToken);
        if (resolved.Error is { } problem)
        {
            return problem;
        }

        var updated = await todos.SetStatusAsync(resolved.Id, parsed, cancellationToken);
        if (updated is null)
        {
            return Error($"No item with id {resolved.Id}.");
        }

        ChangedItemIds.Add(updated.Id.ToString());
        return JsonSerializer.Serialize(new { updated = Summarize(updated) }, Json);
    }

    [Description("Change the priority of an existing item. Accepts the item's id or its exact title.")]
    public async Task<string> SetPriority(
        [Description("The item's id, or its title if the id is unknown.")] string idOrTitle,
        [Description("Low, Medium, High or Critical.")] string priority,
        CancellationToken cancellationToken = default)
    {
        if (ParseEnum<TodoPriority>(priority) is not { } parsed)
        {
            return Error($"'{priority}' is not a valid priority. Use Low, Medium, High or Critical.");
        }

        var resolved = await ResolveAsync(idOrTitle, cancellationToken);
        if (resolved.Error is { } problem)
        {
            return problem;
        }

        var updated = await todos.SetPriorityAsync(resolved.Id, parsed, cancellationToken);
        if (updated is null)
        {
            return Error($"No item with id {resolved.Id}.");
        }

        ChangedItemIds.Add(updated.Id.ToString());
        return JsonSerializer.Serialize(new { updated = Summarize(updated) }, Json);
    }

    [Description("Permanently delete a to-do item. Accepts the item's id or its exact title.")]
    public async Task<string> DeleteTodo(
        [Description("The item's id, or its title if the id is unknown.")] string idOrTitle,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(idOrTitle, cancellationToken);
        if (resolved.Error is { } problem)
        {
            return problem;
        }

        var deleted = await todos.DeleteAsync(resolved.Id, cancellationToken);
        if (!deleted)
        {
            return Error($"No item with id {resolved.Id}.");
        }

        ChangedItemIds.Add(resolved.Id.ToString());
        return JsonSerializer.Serialize(new { deleted = resolved.Id }, Json);
    }

    /// <summary>
    /// People say "mark buy groceries done", not a GUID. Accept either, and refuse rather
    /// than guess when a title matches more than one item.
    /// </summary>
    private async Task<(Guid Id, string? Error)> ResolveAsync(string idOrTitle, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(idOrTitle, out var id))
        {
            return (id, null);
        }

        if (string.IsNullOrWhiteSpace(idOrTitle))
        {
            return (Guid.Empty, Error("Provide the id or the title of the item."));
        }

        var matches = await todos.ListAsync(
            new TodoQuery { Search = idOrTitle.Trim(), PageSize = 10 },
            cancellationToken);

        var exact = matches.Items
            .Where(i => string.Equals(i.Title, idOrTitle.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var candidates = exact.Count > 0 ? exact : matches.Items.ToList();

        return candidates.Count switch
        {
            1 => (candidates[0].Id, null),
            0 => (Guid.Empty, Error($"No item matches '{idOrTitle}'.")),
            _ => (Guid.Empty, JsonSerializer.Serialize(new
            {
                error = $"'{idOrTitle}' matches {candidates.Count} items. Ask the user which one, quoting the titles.",
                candidates = candidates.Select(Summarize)
            }, Json))
        };
    }

    private static object Summarize(TodoResponse item) => new
    {
        id = item.Id,
        title = item.Title,
        status = item.Status.ToString(),
        priority = item.Priority.ToString(),
        dueDate = item.DueDate,
        isOverdue = item.IsOverdue
    };

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { error = message }, Json);

    private static TEnum? ParseEnum<TEnum>(string? value) where TEnum : struct, Enum =>
        !string.IsNullOrWhiteSpace(value) && Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : null;
}
