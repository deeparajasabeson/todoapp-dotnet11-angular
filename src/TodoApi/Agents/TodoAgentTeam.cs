using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using TodoApi.Services;

namespace TodoApi.Agents;

/// <summary>What one chat turn produced.</summary>
public sealed record AgentTurn(string Reply, IReadOnlyList<string> AgentsUsed, IReadOnlyList<string> ChangedItemIds)
{
    public bool ChangedData => ChangedItemIds.Count > 0;
}

/// <summary>
/// A supervisor team: a coordinator agent decides which specialist to consult, and each
/// specialist is exposed to it as a tool. Only the specialists hold the domain tools, so
/// the coordinator cannot, for example, delete an item while "just answering a question".
///
/// Built per request because the task tools write through a scoped
/// <see cref="TodoService"/>; constructing agents is cheap, the model call is not.
/// </summary>
public sealed class TodoAgentTeam(
    IChatClient chatClient,
    TodoService todos,
    KnowledgeBase knowledge,
    IOptions<AiOptions> options,
    ILoggerFactory loggerFactory,
    ILogger<TodoAgentTeam> logger)
{
    private const string CoordinatorName = "Coordinator";

    public async Task<AgentTurn> RunAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        var tools = new TodoTools(todos);
        var agentsUsed = new List<string>();

        var scheduler = BuildScheduler(tools);
        var analyst = BuildAnalyst(tools);
        var guide = BuildGuide();

        var coordinator = new ChatClientAgent(
            chatClient,
            instructions: CoordinatorInstructions(),
            name: CoordinatorName,
            description: "Routes the user's request to the right specialist and answers them.",
            tools:
            [
                Delegate(scheduler, "ask_scheduler",
                    "Use for anything that CHANGES to-do items: creating, completing, re-opening, "
                    + "cancelling, re-prioritising or deleting. Pass the user's intent in plain English.",
                    agentsUsed),
                Delegate(analyst, "ask_analyst",
                    "Use to READ or COUNT to-do items: listing, searching, filtering, what is overdue, "
                    + "how many of something there are. Pass the question in plain English.",
                    agentsUsed),
                Delegate(guide, "ask_guide",
                    "Use for questions about how this application or its API works - statuses, "
                    + "priorities, endpoints, filters, rules. Pass the question in plain English.",
                    agentsUsed)
            ],
            loggerFactory: loggerFactory);

        var response = await coordinator.RunAsync(history, cancellationToken: cancellationToken);

        var reply = response.Text?.Trim();
        if (string.IsNullOrWhiteSpace(reply))
        {
            logger.LogWarning("Coordinator returned an empty reply.");
            reply = "I could not work out a reply to that. Could you rephrase it?";
        }

        return new AgentTurn(reply, agentsUsed.Distinct().ToList(), tools.ChangedItemIds);
    }

    private ChatClientAgent BuildScheduler(TodoTools tools) => new(
        chatClient,
        instructions:
            $"""
            You are the Scheduler. You change to-do items on the user's behalf.
            {TodayLine()}

            Rules:
            - Use the tools for every change; never claim a change you did not make.
            - Convert relative dates ("tomorrow", "next Friday") to an ISO 8601 instant before calling a tool.
            - If a tool reports that a title matches several items, do not guess: report the
              candidate titles back so the user can choose.
            - Report exactly what changed, briefly. No preamble.
            """,
        name: "Scheduler",
        description: "Creates, updates, re-prioritises and deletes to-do items.",
        tools:
        [
            AIFunctionFactory.Create(tools.CreateTodo),
            AIFunctionFactory.Create(tools.SetStatus),
            AIFunctionFactory.Create(tools.SetPriority),
            AIFunctionFactory.Create(tools.DeleteTodo),
            AIFunctionFactory.Create(tools.ListTodos)
        ],
        loggerFactory: loggerFactory);

    private ChatClientAgent BuildAnalyst(TodoTools tools) => new(
        chatClient,
        instructions:
            $"""
            You are the Analyst. You answer questions about the contents of the to-do list.
            {TodayLine()}

            Rules:
            - Always call a tool to get real data. Never invent items, counts or dates.
            - You have read-only tools; if the user wants something changed, say so rather than trying.
            - Prefer a short prose answer. Use a compact list only when naming several items,
              and give each item's title, priority and status.
            """,
        name: "Analyst",
        description: "Answers questions about what is on the list, including counts and overdue work.",
        tools:
        [
            AIFunctionFactory.Create(tools.ListTodos),
            AIFunctionFactory.Create(tools.GetStatistics)
        ],
        loggerFactory: loggerFactory);

    private ChatClientAgent BuildGuide() => new(
        chatClient,
        instructions:
            """
            You are the Guide. You explain how this to-do application and its API work.

            Rules:
            - Call search_knowledge first and answer only from what it returns.
            - If the passages do not cover the question, say so plainly instead of guessing.
            - Be concise and concrete; quote exact field names, statuses and routes.
            """,
        name: "Guide",
        description: "Explains the application's rules, endpoints and behaviour from its documentation.",
        tools: [AIFunctionFactory.Create(SearchKnowledgeAsync)],
        loggerFactory: loggerFactory);

    [Description("Search the application's documentation and return the most relevant passages.")]
    private async Task<string> SearchKnowledgeAsync(
        [Description("What to look up, in the user's own words.")] string query,
        CancellationToken cancellationToken = default)
    {
        var passages = await knowledge.SearchAsync(query, options.Value.RetrievedPassages, cancellationToken);

        return passages.Count == 0
            ? "No documentation passages matched that question."
            : string.Join("\n\n---\n\n", passages.Select(p => p.ToString()));
    }

    /// <summary>
    /// Exposes a specialist agent to the coordinator as a plain function, and records that
    /// it ran so the UI can show which specialists handled the turn.
    /// </summary>
    private static AIFunction Delegate(
        AIAgent specialist,
        string toolName,
        string toolDescription,
        List<string> agentsUsed) =>
        AIFunctionFactory.Create(
            async (string request, CancellationToken cancellationToken) =>
            {
                agentsUsed.Add(specialist.Name ?? toolName);
                var response = await specialist.RunAsync(request, cancellationToken: cancellationToken);
                return response.Text ?? string.Empty;
            },
            toolName,
            toolDescription);

    private static string TodayLine() =>
        $"The current UTC date and time is {DateTimeOffset.UtcNow:yyyy-MM-dd'T'HH:mm:ss'Z'}.";

    private static string CoordinatorInstructions() =>
        $"""
        You are the assistant inside a to-do list web application. You help the user manage
        their list and understand the app, through a narrow chat panel beside the list.
        {TodayLine()}

        You have no knowledge of the user's items yourself. Delegate:
        - ask_scheduler to change anything
        - ask_analyst to read, count or search anything
        - ask_guide for questions about how the app or API works

        Rules:
        - Never invent items, counts, dates or behaviour. If you did not get it from a
          specialist, you do not know it.
        - You may call more than one specialist for a compound request, and you may call one
          again if its answer is incomplete.
        - Before deleting anything, make sure the user actually asked for a deletion; if the
          request is ambiguous, ask first.
        - Reply in plain prose, two or three sentences at most, in the panel's narrow column.
          Use a short bulleted list only when naming several items. No headings, no preamble.
        """;
}
