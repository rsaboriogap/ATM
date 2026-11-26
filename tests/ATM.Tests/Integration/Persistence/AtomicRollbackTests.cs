using ATM.Application.DTOs;
using ATM.Application.Services;
using ATM.Domain.Enums;
using ATM.Infrastructure.Persistence;
using ATM.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ATM.Tests.Integration.Persistence;

public sealed class AtomicRollbackTests
{
    [Fact]
    public async Task Transfer_PersistenceFailure_RollsBackEverything()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        Guid sourceId;
        Guid destinationId;

        await using (var setup = database.CreateContext())
        {
            var accounts = await setup.Accounts
                .AsNoTracking()
                .OrderBy(a => a.Name)
                .ToListAsync();

            sourceId = accounts[0].Id;
            destinationId = accounts[1].Id;

            var transferIn = (int)TransactionType.TransferIn;

            await setup.Database.ExecuteSqlRawAsync(
                $"""
                CREATE TRIGGER FailTransferIn
                BEFORE INSERT ON Transactions
                WHEN NEW.Type = {transferIn}
                BEGIN
                    SELECT RAISE(
                        ABORT,
                        'Simulated transfer persistence failure'
                    );
                END;
                """);
        }

        await using (var context = database.CreateContext())
        {
            var service = new AtmService(
                new AccountRepository(context),
                new TransactionRepository(context),
                new EfUnitOfWork(context));

            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.TransferAsync(
                    new TransferRequest(
                        sourceId,
                        destinationId,
                        300m)));

            Assert.Empty(context.ChangeTracker.Entries());
        }

        await using var verification =
            database.CreateContext();

        var source = await verification.Accounts
            .SingleAsync(a => a.Id == sourceId);

        var destination = await verification.Accounts
            .SingleAsync(a => a.Id == destinationId);

        Assert.Equal(1000m, source.Balance);
        Assert.Equal(1000m, destination.Balance);

        Assert.Equal(
            0,
            await verification.Transactions.CountAsync());
    }
}