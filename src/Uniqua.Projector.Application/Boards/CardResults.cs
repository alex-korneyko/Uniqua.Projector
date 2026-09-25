using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// What <see cref="OpenCard"/> and <see cref="EditCard"/> hand back (contracts/openapi.yaml,
/// components.schemas.Card, abridged): a card's full text, on demand (ADR 0018) — unlike
/// <see cref="Uniqua.Projector.Application.Boards.Ports.CardSummary"/>, which a board open uses and
/// which carries no description.
/// </summary>
public sealed record CardView(
    Guid Id, Guid ColumnId, int Position, string Title, string Description, int ContentVersion);

/// <summary>The one mapping from the aggregate's card to the shape the card use cases return.</summary>
internal static class CardViews
{
    public static CardView Of(Card card) =>
        new(card.Id, card.ColumnId, card.Position, card.Title, card.Description, card.ContentVersion);
}
