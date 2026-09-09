using System.ComponentModel.DataAnnotations;

namespace TodoApi.Contracts;

/// <summary>A single turn from the user.</summary>
public record ChatRequest(
    [property: Required, MaxLength(2000)] string Message,
    [property: MaxLength(64)] string? ConversationId = null);

/// <summary>The assistant's answer, plus what it did along the way.</summary>
public record ChatReply(
    string ConversationId,
    string Reply,
    IReadOnlyList<string> AgentsUsed,
    bool ChangedData,
    IReadOnlyList<string> ChangedItemIds);

/// <summary>Lets the UI show a useful message when the assistant is not wired up.</summary>
public record ChatStatus(bool Available, string? Model, string? Detail);
