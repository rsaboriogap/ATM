using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ATM.Infrastructure.Persistence.Converters;

public sealed class MoneyValueConverter
    : ValueConverter<decimal, long>
{
    public MoneyValueConverter()
        : base(
            amount => checked((long)(amount * 100m)),
            cents => cents / 100m)
    {
    }
}