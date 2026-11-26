using ATM.Infrastructure.Persistence;
using ATM.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ATM.Tests.Integration.Persistence;

public sealed class AccountRepositoryTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsTwoAccounts()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var repository = new AccountRepository(context);

        var accounts = await repository.GetAllAsync();

        Assert.Equal(2, accounts.Count);

        Assert.Contains(
            accounts, account => account.Name == "Checking");

        Assert.Contains(
            accounts, account => account.Name == "Savings");

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetByIdAsync_ExistingAccount_IsNotTracked()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var repository = new AccountRepository(context);

        var accounts = await repository.GetAllAsync();

        var account = await repository.GetByIdAsync(
            accounts[0].Id);

        Assert.NotNull(account);

        Assert.Equal(
            EntityState.Detached,
            context.Entry(account).State);

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetForUpdateAsync_ExistingAccount_IsTracked()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var repository = new AccountRepository(context);
        var unitOfWork = new EfUnitOfWork(context);

        var accounts = await repository.GetAllAsync();

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var account = await repository.GetForUpdateAsync(
                accounts[0].Id, ct);

            Assert.NotNull(account);

            Assert.Equal(
                EntityState.Unchanged,
                context.Entry(account).State);

            return true;
        });
    }

    [Fact]
    public async Task GetByIdAsync_UnknownAccount_ReturnsNull()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var repository = new AccountRepository(context);

        var account = await repository.GetByIdAsync(
            Guid.NewGuid());

        Assert.Null(account);
    }

    [Fact]
    public async Task GetForUpdateAsync_WithoutTransaction_ThrowsException()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var repository = new AccountRepository(context);

        var accounts = await repository.GetAllAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await repository.GetForUpdateAsync(
                accounts[0].Id));
    }
}