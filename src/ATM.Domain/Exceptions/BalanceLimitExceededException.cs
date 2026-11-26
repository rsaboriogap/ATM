using System;
using System.Collections.Generic;
using System.Text;

namespace ATM.Domain.Exceptions;

public sealed class BalanceLimitExceededException
    : DomainException
{
    public BalanceLimitExceededException()
        : base(
            "BALANCE_LIMIT_EXCEEDED",
            "The maximum account balance would be exceeded.")
    {
    }
}