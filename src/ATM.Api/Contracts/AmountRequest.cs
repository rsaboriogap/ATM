using System.ComponentModel.DataAnnotations;

namespace ATM.Api.Contracts;

public sealed record AmountRequest(
    [property: Required]
    decimal? Amount);