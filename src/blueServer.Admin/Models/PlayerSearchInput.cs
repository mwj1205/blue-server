using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace blueServer.Admin.Models;

public sealed class PlayerSearchInput : IValidatableObject
{
    [Required(ErrorMessage = "Player ID를 입력하세요.")]
    public string? PlayerId { get; set; }

    public bool TryGetPlayerId(out long playerId)
    {
        return long.TryParse(
                PlayerId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out playerId) &&
            playerId > 0;
    }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(PlayerId) ||
            TryGetPlayerId(out _))
        {
            yield break;
        }

        yield return new ValidationResult(
            "Player ID는 1 이상의 정수여야 합니다.",
            [nameof(PlayerId)]);
    }
}
