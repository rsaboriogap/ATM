using System;
using System.Collections.Generic;
using System.Text;

namespace ATM.Domain.Exceptions;

public sealed class InsufficientFundsException : DomainException
{
    public InsufficientFundsException()
        : base(
            "INSUFFICIENT_FUNDS",
            "The account has insufficient funds.")
    {
    }
}