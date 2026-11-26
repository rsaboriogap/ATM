using System;
using System.Collections.Generic;
using System.Text;

namespace ATM.Domain.Exceptions;

public abstract class DomainException : Exception
{
    public string Code { get; }

    protected DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }
}