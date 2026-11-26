using System.ComponentModel.DataAnnotations;
using ATM.Client.Validation;

namespace ATM.Client.Models;

public sealed class AccountOperationFormModel
{
    [Required(ErrorMessage = "Select an account.")]
    public Guid? AccountId { get; set; }

    [Required(ErrorMessage = "Enter an amount.")]
    [MoneyAmount]
    public decimal? Amount { get; set; }
}