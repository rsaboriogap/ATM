using System;
using System.Collections.Generic;
using System.Text;

namespace ATM.Domain.Enums;

public enum TransactionType
{
    Deposit = 1,
    Withdrawal = 2,
    TransferIn = 3,
    TransferOut = 4,
    OpeningBalance = 5
}
