namespace Uniqua.Projector.Application.Accounts.Ports;

/// <summary>
/// The failure count for addresses no account owns. AC-05b requires that "neither the message nor
/// the wait reveals whether the address is registered": once AC-12's curve starts holding a
/// registered address back, an unregistered one has to be held back the same way, or the wait
/// alone becomes the oracle.
/// </summary>
/// <remarks>
/// It is deliberately not the account store. There is no account row to count against, and
/// writing one per guessed address would store the guesses themselves — so the count lives in
/// memory only, keyed so that the address is never held in the clear.
/// </remarks>
public interface IUnknownAddressAttempts
{
    /// <summary>
    /// Records one failed attempt against an address no account owns and returns the number it
    /// carries, counted by the same rule — and reset by the same 15 quiet minutes — as a
    /// registered account's.
    /// </summary>
    int RecordFailure(string email, DateTimeOffset now);
}
