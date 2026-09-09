using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using TodoApi.Agents;
using TodoApi.Contracts;
using TodoApi.Infrastructure;

namespace TodoApi.Endpoints;

public static class ChatEndpoints
{
    public static RouteGroupBuilder MapChatEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/chat")
            .WithTags("Chat");

        group.MapGet("/status", GetStatus)
            .WithName("GetChatStatus")
            .WithSummary("Whether the assistant is configured, and which model it uses.");

        group.MapPost("/", SendAsync)
            .WithValidation<ChatRequest>()
            .WithName("SendChatMessage")
            .WithSummary("Send a message to the multi-agent assistant.");

        return group;
    }

    private static Ok<ChatStatus> GetStatus(IOptions<AiOptions> options)
    {
        var ai = options.Value;

        return TypedResults.Ok(ai.IsConfigured
            ? new ChatStatus(true, ai.ChatDeployment, null)
            : new ChatStatus(false, null,
                "Set Ai:Endpoint and Ai:ApiKey (user secrets or environment) to enable the assistant."));
    }

    private static async Task<Results<Ok<ChatReply>, ProblemHttpResult>> SendAsync(
        ChatRequest request,
        IOptions<AiOptions> options,
        ConversationStore conversations,
        // Resolved lazily so the endpoint still answers when the assistant is not configured.
        IServiceProvider services,
        ILogger<ChatReply> logger,
        CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
        {
            return TypedResults.Problem(
                title: "Assistant not configured",
                detail: "Set Ai:Endpoint and Ai:ApiKey to enable the chat assistant.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.CreateVersion7().ToString()
            : request.ConversationId;

        var history = conversations.Append(conversationId, new ChatMessage(ChatRole.User, request.Message));

        try
        {
            var team = services.GetRequiredService<TodoAgentTeam>();
            var turn = await team.RunAsync(history, cancellationToken);

            conversations.Append(conversationId, new ChatMessage(ChatRole.Assistant, turn.Reply));

            return TypedResults.Ok(new ChatReply(
                conversationId,
                turn.Reply,
                turn.AgentsUsed,
                turn.ChangedData,
                turn.ChangedItemIds));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A model outage should read as "the assistant is down", not as a broken app.
            logger.LogError(ex, "Chat turn failed for conversation {ConversationId}.", conversationId);

            return TypedResults.Problem(
                title: "The assistant could not answer",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
