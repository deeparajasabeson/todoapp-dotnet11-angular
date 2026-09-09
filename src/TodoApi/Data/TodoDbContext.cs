using Microsoft.EntityFrameworkCore;
using TodoApi.Models;

namespace TodoApi.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<TodoItem> Todos => Set<TodoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var todo = modelBuilder.Entity<TodoItem>();

        todo.HasKey(t => t.Id);
        todo.Property(t => t.Title).IsRequired().HasMaxLength(200);
        todo.Property(t => t.Description).HasMaxLength(2000);

        // Kept as the underlying int so that "order by priority" means
        // Low -> Critical on any provider. The JSON contract still uses names.
        todo.Property(t => t.Status).HasConversion<int>();
        todo.Property(t => t.Priority).HasConversion<int>();

        todo.HasIndex(t => t.Status);
        todo.HasIndex(t => t.Priority);
        todo.HasIndex(t => t.DueDate);
    }
}
