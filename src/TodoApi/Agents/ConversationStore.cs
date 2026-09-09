using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace TodoApi.Agents;

/// <summary>
/// In-memory chat history, keyed by conversation id. Deliberately not durable: a browser
/// refresh starting a fresh conversation is fine, and nothing here is worth a database.
/// Old conversations are evicted so a long-running process cannot grow without bound.
/// </summary>
public sealed class ConversationStore
{
    private const int MaxConversations = 200;
    private const int MaxMessagesKept = 20;
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<string, Conversation> _conversations = new();

    public IReadOnlyList<ChatMessage> Append(string conversationId, ChatMessage message)
    {
        var conversation = _conversations.GetOrAdd(conversationId, _ => new Conversation());

        lock (conversation.Gate)
        {
            conversation.LastUsed = DateTimeOffset.UtcNow;
            conversation.Messages.Add(message);

            // Keep the tail only; the coordinator re-reads the whole history each turn.
            if (conversation.Messages.Count > MaxMessagesKept)
            {
                conversation.Messages.RemoveRange(0, conversation.Messages.Count - MaxMessagesKept);
            }

            Evict();
            return conversation.Messages.ToList();
        }
    }

    private void Evict()
    {
        var cutoff = DateTimeOffset.UtcNow - Lifetime;

        foreach (var (id, conversation) in _conversations)
        {
            if (conversation.LastUsed < cutoff)
            {
                _conversations.TryRemove(id, out _);
            }
        }

        if (_conversations.Count <= MaxConversations)
        {
            return;
        }

        foreach (var (id, _) in _conversations.OrderBy(c => c.Value.LastUsed).Take(_conversations.Count - MaxConversations))
        {
            _conversations.TryRemove(id, out _);
        }
    }

    private sealed class Conversation
    {
        public object Gate { get; } = new();
        public List<ChatMessage> Messages { get; } = [];
        public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.UtcNow;
    }
}
