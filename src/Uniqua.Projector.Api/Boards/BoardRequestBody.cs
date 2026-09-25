using System.Text.Json;

namespace Uniqua.Projector.Api.Boards;

/// <summary>
/// A board request body bound leniently (ADR 0014): whatever JSON arrived, kept as it arrived. The
/// framework's own binding of the underlying <see cref="JsonElement"/> refuses only a body that is
/// not JSON at all — before any board is looked at, so identically for every board, and reshaped
/// into <c>boards.request_malformed</c> by <see cref="ProblemDetailsSetup"/>. Everything else about
/// the shape is judged by the handler through the getters here, and only once the member-scoped
/// load has admitted the caller, so a non-member's malformed request is never told apart from any
/// other non-member's (AC-25).
/// </summary>
public readonly struct BoardRequestBody(JsonElement body)
{
    /// <summary>
    /// The string member <paramref name="name"/>, or <see langword="false"/> when the body is not
    /// an object, the member is absent, or it is not a string — the contract's
    /// <c>boards.request_invalid</c>.
    /// </summary>
    public bool TryGetString(string name, out string value)
    {
        if (body.ValueKind is JsonValueKind.Object
            && body.TryGetProperty(name, out var member)
            && member.ValueKind is JsonValueKind.String)
        {
            value = member.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// The whole-number member <paramref name="name"/> that fits an <see cref="int"/>, or
    /// <see langword="false"/> when the body is not an object, the member is absent, or it is not
    /// such a number — the contract's <c>boards.request_invalid</c>.
    /// </summary>
    public bool TryGetInt32(string name, out int value)
    {
        value = 0;
        return body.ValueKind is JsonValueKind.Object
            && body.TryGetProperty(name, out var member)
            && member.ValueKind is JsonValueKind.Number
            && member.TryGetInt32(out value);
    }
}
