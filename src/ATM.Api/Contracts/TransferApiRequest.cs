using System.ComponentModel.DataAnnotations;

namespace ATM.Api.Contracts;

public sealed record TransferApiRequest(
    [property: Required]
    Guid? SourceAccountId,

    [property: Required]
    Guid? DestinationAccountId,

    [property: Required]
    decimal? Amount);