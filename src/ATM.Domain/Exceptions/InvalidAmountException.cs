using System;
using System.Collections.Generic;
using System.Text;

namespace ATM.Domain.Exceptions;

public sealed class InvalidAmountException : DomainException
{
    public InvalidAmountException(string message)
        : base("INVALID_AMOUNT", message)
    {
    }
}