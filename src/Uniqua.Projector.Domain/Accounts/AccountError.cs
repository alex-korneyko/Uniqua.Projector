namespace Uniqua.Projector.Domain.Accounts;

/// <summary>
/// A refusal, carrying exactly the plain-language reason its acceptance criterion specifies and no
/// more (sad.md § 8). The <see cref="Code"/> is the contract's <c>accounts.*</c> identifier, so the
/// endpoint translates a refusal rather than deciding what it means.
/// </summary>
/// <param name="Code">The <c>accounts.*</c> code from contracts/openapi.yaml.</param>
/// <param name="Detail">The sentence shown to whoever made the request.</param>
public sealed record AccountError(string Code, string Detail);
