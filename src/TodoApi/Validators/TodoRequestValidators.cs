using FluentValidation;
using TodoApi.Contracts;

namespace TodoApi.Validators;

/// <summary>
/// Limits shared by create and replace, kept next to each other so the two payloads
/// cannot drift apart.
/// </summary>
internal static class TodoRules
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
}

public sealed class CreateTodoRequestValidator : AbstractValidator<CreateTodoRequest>
{
    public CreateTodoRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(TodoRules.TitleMaxLength)
            .WithMessage($"Title must be {TodoRules.TitleMaxLength} characters or fewer.");

        RuleFor(x => x.Description)
            .MaximumLength(TodoRules.DescriptionMaxLength)
            .WithMessage($"Description must be {TodoRules.DescriptionMaxLength} characters or fewer.");

        // Both are optional on create - the API supplies Pending/Medium - so only
        // check the value when one was actually sent.
        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Value is not a valid status.")
            .When(x => x.Status.HasValue);

        RuleFor(x => x.Priority)
            .IsInEnum().WithMessage("Value is not a valid priority.")
            .When(x => x.Priority.HasValue);
    }
}

public sealed class UpdateTodoRequestValidator : AbstractValidator<UpdateTodoRequest>
{
    public UpdateTodoRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(TodoRules.TitleMaxLength)
            .WithMessage($"Title must be {TodoRules.TitleMaxLength} characters or fewer.");

        RuleFor(x => x.Description)
            .MaximumLength(TodoRules.DescriptionMaxLength)
            .WithMessage($"Description must be {TodoRules.DescriptionMaxLength} characters or fewer.");

        // A replace always states both, so unlike create these are unconditional.
        RuleFor(x => x.Status).IsInEnum().WithMessage("Value is not a valid status.");
        RuleFor(x => x.Priority).IsInEnum().WithMessage("Value is not a valid priority.");
    }
}

public sealed class UpdateStatusRequestValidator : AbstractValidator<UpdateStatusRequest>
{
    public UpdateStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum().WithMessage("Value is not a valid status.");
    }
}

public sealed class UpdatePriorityRequestValidator : AbstractValidator<UpdatePriorityRequest>
{
    public UpdatePriorityRequestValidator()
    {
        RuleFor(x => x.Priority).IsInEnum().WithMessage("Value is not a valid priority.");
    }
}
