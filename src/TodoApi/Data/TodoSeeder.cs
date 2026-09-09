using Microsoft.EntityFrameworkCore;
using TodoApi.Models;

namespace TodoApi.Data;

/// <summary>Puts a handful of items in the store so the API is not empty on first run.</summary>
public static class TodoSeeder
{
    public static async Task SeedAsync(TodoDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Todos.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        db.Todos.AddRange(
            new TodoItem
            {
                Title = "Read the ASP.NET Core minimal API docs",
                Description = "Focus on route groups, filters and results.",
                Status = TodoStatus.InProgress,
                Priority = TodoPriority.High,
                DueDate = now.AddDays(2),
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddDays(-1)
            },
            new TodoItem
            {
                Title = "Buy groceries",
                Description = "Milk, eggs, coffee.",
                Status = TodoStatus.Pending,
                Priority = TodoPriority.Low,
                DueDate = now.AddDays(1),
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddDays(-1)
            },
            new TodoItem
            {
                Title = "Renew domain name",
                Description = "Expires soon - do not let it lapse.",
                Status = TodoStatus.Pending,
                Priority = TodoPriority.Critical,
                DueDate = now.AddDays(-1),
                CreatedAt = now.AddDays(-5),
                UpdatedAt = now.AddDays(-5)
            },
            new TodoItem
            {
                Title = "Set up the project repository",
                Status = TodoStatus.Completed,
                Priority = TodoPriority.Medium,
                CreatedAt = now.AddDays(-3),
                UpdatedAt = now.AddDays(-2),
                CompletedAt = now.AddDays(-2)
            });

        await db.SaveChangesAsync(cancellationToken);
    }
}
