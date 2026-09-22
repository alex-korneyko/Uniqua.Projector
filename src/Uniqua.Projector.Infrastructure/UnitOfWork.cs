using Microsoft.EntityFrameworkCore;
using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Infrastructure;

/// <summary>
/// The transaction behind <see cref="IUnitOfWork"/>. It lives here because only Infrastructure may
/// know that a transaction is what makes several port calls one outcome.
/// </summary>
internal sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
    {
        // The execution strategy owns the retry policy, and a retry has to replay the whole block
        // rather than part of it — which is why the transaction is opened inside the strategy and
        // not around it.
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var result = await work(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
