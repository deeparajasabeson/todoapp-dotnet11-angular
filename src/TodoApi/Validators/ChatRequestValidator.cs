using FluentValidation;
using TodoApi.Contracts;

namespace TodoApi.Validators;

public sealed class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public ChatRequestValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Type a message first.")
            .MaximumLength(2000).WithMessage("Message must be 2000 characters or fewer.");

        // Ids are server-generated; a long or odd one is a client bug, not a conversation.
        RuleFor(x => x.ConversationId)
            .MaximumLength(64).WithMessage("Conversation id must be 64 characters or fewer.");
    }
}
