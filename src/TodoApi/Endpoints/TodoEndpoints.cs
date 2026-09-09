using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using TodoApi.Contracts;
using TodoApi.Infrastructure;
using TodoApi.Services;

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
            .WithValidation<CreateTodoRequest>()
            .WithName("CreateTodo")
            .WithSummary("Create a to-do item.");

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithValidation<UpdateTodoRequest>()
            .WithName("UpdateTodo")
            .WithSummary("Replace a to-do item.");

        group.MapPatch("/{id:guid}/status", UpdateStatusAsync)
            .WithValidation<UpdateStatusRequest>()
            .WithName("UpdateTodoStatus")
            .WithSummary("Move a to-do item to a new status.");

        group.MapPatch("/{id:guid}/priority", UpdatePriorityAsync)
            .WithValidation<UpdatePriorityRequest>()
            .WithName("UpdateTodoPriority")
            .WithSummary("Change the priority of a to-do item.");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteTodo")
            .WithSummary("Delete a to-do item.");

        return group;
    }

    private static async Task<Ok<PagedResponse<TodoResponse>>> ListAsync(
        [AsParameters] TodoQuery query,
        TodoService todos,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await todos.ListAsync(query, cancellationToken));

    private static async Task<Results<Ok<TodoResponse>, NotFound<ProblemDetails>>> GetAsync(
        Guid id,
        TodoService todos,
        CancellationToken cancellationToken)
    {
        var item = await todos.GetAsync(id, cancellationToken);

        return item is null
            ? TypedResults.NotFound(NotFoundProblem(id))
            : TypedResults.Ok(item);
    }

    private static async Task<Created<TodoResponse>> CreateAsync(
        CreateTodoRequest request,
        TodoService todos,
        CancellationToken cancellationToken)
    {
        var item = await todos.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"/api/todos/{item.Id}", item);
    }

    private static async Task<Results<Ok<TodoResponse>, NotFound<ProblemDetails>>> UpdateAsync(
        Guid id,
        UpdateTodoRequest request,
        TodoService todos,
        CancellationToken cancellationToken)
    {
        var item = await todos.ReplaceAsync(id, request, cancellationToken);

        return item is null
            ? TypedResults.NotFound(NotFoundProblem(id))
            : TypedResults.Ok(item);
    }

    private static async Task<Results<Ok<TodoResponse>, NotFound<ProblemDetails>>> UpdateStatusAsync(
        Guid id,
        UpdateStatusRequest request,
        TodoService todos,
        CancellationToken cancellationToken)
    {
        var item = await todos.SetStatusAsync(id, request.Status, cancellationToken);

        return item is null
            ? TypedResults.NotFound(NotFoundProblem(id))
            : TypedResults.Ok(item);
    }

    private static async Task<Results<Ok<TodoResponse>, NotFound<ProblemDetails>>> UpdatePriorityAsync(
        Guid id,
        UpdatePriorityRequest request,
        TodoService todos,
        CancellationToken cancellationToken)
    {
        var item = await todos.SetPriorityAsync(id, request.Priority, cancellationToken);

        return item is null
            ? TypedResults.NotFound(NotFoundProblem(id))
            : TypedResults.Ok(item);
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>>> DeleteAsync(
        Guid id,
        TodoService todos,
        CancellationToken cancellationToken) =>
        await todos.DeleteAsync(id, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound(NotFoundProblem(id));

    private static ProblemDetails NotFoundProblem(Guid id) => new()
    {
        Title = "To-do item not found",
        Detail = $"No to-do item exists with id '{id}'.",
        Status = StatusCodes.Status404NotFound
    };
}
