using ATM.Domain.Entities;
using ATM.Domain.Enums;
using ATM.Domain.Exceptions;
using ATM.Infrastructure.Persistence;
using ATM.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ATM.Tests.Integration.Persistence;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task ConcurrentWithdrawals_CannotOverdrawAccount()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        Guid accountId;

        await using (var setupContext = database.CreateContext())
        {
            accountId = await setupContext.Accounts
                .Where(a => a.Name == "Checking")
                .Select(a => a.Id)
                .SingleAsync();
        }

        var firstHasLock = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var secondStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> WithdrawAsync(bool isFirst)
        {
            await using var context = database.CreateContext();

            var accounts = new AccountRepository(context);
            var transactions = new TransactionRepository(context);
            var unitOfWork = new EfUnitOfWork(context);

            try
            {
                if (!isFirst)
                {
                    secondStarted.TrySetResult(true);
                }

                return await unitOfWork
                    .ExecuteInTransactionAsync(async ct =>
                    {
                        var account = await accounts
                            .GetForUpdateAsync(accountId, ct);

                        if (isFirst)
                        {
                            firstHasLock.TrySetResult(true);

                            await secondStarted.Task.WaitAsync(
                                TimeSpan.FromSeconds(3));

                            // Keep the first transaction open
                            // briefly to exercise lock contention.
                            await Task.Delay(200, ct);
                        }

                        account!.Withdraw(800m);

                        transactions.Add(
                            Transaction.Create(
                                account.Id,
                                TransactionType.Withdrawal,
                                800m,
                                account.Balance));

                        return true;
                    });
            }
            catch (InsufficientFundsException)
            {
                return false;
            }
        }

        var first = Task.Run(() => WithdrawAsync(true));

        await firstHasLock.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        var second = Task.Run(() => WithdrawAsync(false));

        var results = await Task.WhenAll(first, second);

        Assert.Single(results.Where(result => result));
        Assert.Single(results.Where(result => !result));

        await using var verification =
            database.CreateContext();

        var account = await verification.Accounts
            .SingleAsync(a => a.Id == accountId);

        Assert.Equal(200m, account.Balance);

        Assert.Equal(
            1,
            await verification.Transactions.CountAsync());
    }
}