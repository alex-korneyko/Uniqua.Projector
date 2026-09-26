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
/// <remarks>
/// No getter throws on anything JSON can carry: a value that cannot be read as asked is simply the
/// contract's <c>boards.request_invalid</c>, answered in the same post-membership branch as any
/// other wrong shape.
/// </remarks>
public readonly struct BoardRequestBody(JsonElement body)
{
    /// <summary>
    /// The string member <paramref name="name"/>, or <see langword="false"/> when the body is not
    /// an object, the member is absent, or it is not a string — the contract's
    /// <c>boards.request_invalid</c>.
    /// </summary>
    /// <remarks>
    /// A JSON string escaping a lone surrogate (<c>"\ud800"</c>) is valid JSON but no valid UTF-16
    /// text, and <see cref="JsonElement.GetString"/> refuses it by throwing. It is not a string this
    /// application can store, so it is answered as a member that is not a string (review Q1).
    /// </remarks>
    public bool TryGetString(string name, out string value)
    {
        if (body.ValueKind is JsonValueKind.Object
            && body.TryGetProperty(name, out var member)
            && member.ValueKind is JsonValueKind.String)
        {
            try
            {
                value = member.GetString()!;
                return true;
            }
            catch (InvalidOperationException)
            {
                // A lone surrogate: not text; falls through to the not-a-string answer.
            }
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// An optional string member: absent is <see langword="null"/> and fine; present, it must be a
    /// string, or the request is the contract's <c>boards.request_invalid</c>. A body that is not an
    /// object is refused outright.
    /// </summary>
    public bool TryGetOptionalString(string name, out string? value)
    {
        value = null;
        if (body.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        if (!body.TryGetProperty(name, out _))
        {
            return true;
        }

        if (!TryGetString(name, out var present))
        {
            return false;
        }

        value = present;
        return true;
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

    /// <summary>
    /// <see langword="false"/> when the body is an object carrying a member outside
    /// <paramref name="names"/>. Every request schema in the contract is
    /// <c>additionalProperties: false</c>, and an unknown member is <c>boards.request_invalid</c>
    /// (contracts/api-sync-report.md OQ-API-3; review B6a). A body that is no object at all is left
    /// to the getters, which refuse it.
    /// </summary>
    /// <remarks>
    /// Names are compared with <see cref="JsonProperty.NameEquals(string?)"/> rather than read
    /// through <see cref="JsonProperty.Name"/>, which throws on a lone-surrogate member name.
    /// </remarks>
    public bool HasOnly(params ReadOnlySpan<string> names)
    {
        if (body.ValueKind is not JsonValueKind.Object)
        {
            return true;
        }

        foreach (var member in body.EnumerateObject())
        {
            var known = false;
            foreach (var name in names)
            {
                if (member.NameEquals(name))
                {
                    known = true;
                    break;
                }
            }

            if (!known)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A column or card path id (or <c>addCard</c>'s <c>column_id</c>): one that is not a UUID names
    /// no item on this board. <see cref="Guid.Empty"/> is never an id (<c>Ids.New()</c> issues v7
    /// only), so the Board answers it as any absent item — after the membership and shape checks,
    /// exactly as a well-formed id that does not exist (contracts/openapi.yaml,
    /// components.parameters.ColumnId / CardId; review B6c).
    /// </summary>
    public static Guid ItemId(string text) => Guid.TryParse(text, out var id) ? id : Guid.Empty;
}
