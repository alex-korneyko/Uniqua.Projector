namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// Signals that a structural change lost a race on a board's, a column's, a card's or the
/// owned-board counter's version (ADR 0015). Application-defined, so no EF Core concurrency
/// exception type crosses into Application — <c>IBoardStore.SaveAsync</c> and
/// <c>IOwnedBoardCounterStore.SaveAsync</c> translate into this rather than letting
/// <c>DbUpdateConcurrencyException</c> (or a primary-key violation on the counter's first insert)
/// escape Infrastructure (CLAUDE.md § Persistence is EF Core, behind ports).
/// </summary>
public sealed class BoardConcurrencyConflict()
    : Exception("The board changed since it was loaded; the change must be retried.");

/// <summary>
/// Whether a retried change was ever applied, the value it produced if so, and how many attempts it
/// took (ADR 0015). <see cref="Value"/> is meaningless when <see cref="Contended"/> is <c>true</c>.
/// </summary>
public sealed record BoardChangeOutcome<T>(bool Contended, T? Value, int Attempts)
{
    public static BoardChangeOutcome<T> Applied(T value, int attempts) => new(false, value, attempts);

    public static BoardChangeOutcome<T> Contested(int attempts) => new(true, default, attempts);
}

/// <summary>
/// Re-runs a whole load -&gt; decide -&gt; save cycle up to <see cref="MaxAttempts"/> times whenever
/// it loses to a <see cref="BoardConcurrencyConflict"/>, then gives up rather than looping forever
/// (ADR 0015, Consequences: "after 3 conflicts the change fails with a retryable problem"). Each
/// attempt must itself perform the whole cycle — nothing carries over between attempts, since the
/// point of retrying is to re-decide against state that has moved on.
/// </summary>
public static class BoardChangeRetry
{
    /// <summary>
    /// ADR 0015: 3 attempts (2 retries) before the change fails with a retryable problem.
    /// </summary>
    public const int MaxAttempts = 3;

    /// <param name="attempt">The whole cycle, given its 1-based attempt number.</param>
    public static async Task<BoardChangeOutcome<T>> RunAsync<T>(Func<int, Task<T>> attempt)
    {
        for (var number = 1; number <= MaxAttempts; number++)
        {
            try
            {
                return BoardChangeOutcome<T>.Applied(await attempt(number), number);
            }
            catch (BoardConcurrencyConflict)
            {
                // Lost the race: the next attempt reloads and re-decides from scratch.
            }
        }

        return BoardChangeOutcome<T>.Contested(MaxAttempts);
    }
}
