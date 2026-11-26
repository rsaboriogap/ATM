using System.ComponentModel.DataAnnotations;
using ATM.Client.Validation;

namespace ATM.Client.Models;

public sealed class TransferFormModel : IValidatableObject
{
    [Required(ErrorMessage = "Select a source account.")]
    public Guid? SourceAccountId { get; set; }

    [Required(ErrorMessage = "Select a destination account.")]
    public Guid? DestinationAccountId { get; set; }

    [Required(ErrorMessage = "Enter an amount.")]
    [MoneyAmount]
    public decimal? Amount { get; set; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (SourceAccountId.HasValue
            && DestinationAccountId.HasValue
            && SourceAccountId == DestinationAccountId)
        {
            yield return new ValidationResult(
                "Source and destination accounts must be different.",
                new[] { nameof(DestinationAccountId) });
        }
    }
}