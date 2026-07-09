using blueServer.Api.DTOs;
using blueServer.Domain.Entities;
using FluentValidation;

namespace blueServer.Api.Validators;

public class SavePartyRequestValidator : AbstractValidator<SavePartyRequest>
{
    public SavePartyRequestValidator()
    {
        RuleFor(request => request.Name)
            .MaximumLength(20)
            .WithMessage("Party name must be at most 20 characters");

        RuleFor(request => request.Slots)
            .NotNull()
            .WithMessage("Slots are required")
            .Must(slots => slots is null || slots.Count <= PartySlot.MaxSlotIndex)
            .WithMessage($"Slots must be at most {PartySlot.MaxSlotIndex}");

        RuleForEach(request => request.Slots)
            .ChildRules(slot =>
            {
                slot.RuleFor(x => x.SlotIndex)
                    .InclusiveBetween(
                        PartySlot.MinSlotIndex,
                        PartySlot.MaxSlotIndex)
                    .WithMessage(
                        $"Slot index must be between {PartySlot.MinSlotIndex} and {PartySlot.MaxSlotIndex}");

                slot.RuleFor(x => x.OwnedCharacterId)
                    .GreaterThan(0)
                    .WithMessage("Owned character id must be greater than zero");
            });

        RuleFor(request => request.Slots)
            .Must(slots => slots is null || slots
                .Select(slot => slot.SlotIndex)
                .Distinct()
                .Count() == slots.Count)
            .WithMessage("Slot index must not be duplicated");

        RuleFor(request => request.Slots)
            .Must(slots => slots is null || slots
                .Select(slot => slot.OwnedCharacterId)
                .Distinct()
                .Count() == slots.Count)
            .WithMessage("Owned character id must not be duplicated");
    }
}
