using blueServer.Api.DTOs;
using FluentValidation;

namespace blueServer.Api.Validators;

public class CreatePlayerRequestValidator
    : AbstractValidator<CreatePlayerRequest>
{
    public CreatePlayerRequestValidator()
    {
        RuleFor(x => x.Nickname)
            .NotEmpty()
            .WithMessage("Nickname is required")

            .MinimumLength(3)
            .WithMessage("Nickname must be at least 3 characters")

            .MaximumLength(10)
            .WithMessage("Nickname must be at most 10 characters");
    }
}