using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Application.Boards;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Api.Boards;

/// <summary>
/// The card endpoints on the board group: <c>addCard</c>, <c>openCard</c>, <c>editCard</c> and
/// <c>deleteCard</c> (contracts/openapi.yaml, paths <c>/api/v1/boards/{boardId}/cards*</c>). Card
/// text is returned exactly as stored — plain text in a JSON string, never reshaped (AC-16).
/// </summary>
/// <remarks>
/// A stale edit or delete is answered <c>boards.card_changed</c> with <c>current_card</c> — the card
/// as it now stands, read afterwards through the same member-scoped load, so it only ever reaches a
/// member, and a card gone in the meantime is answered as any absent one is (AC-23).
/// </remarks>
public static partial class BoardEndpoints
{
    private static RouteGroupBuilder MapCardEndpoints(this RouteGroupBuilder boards)
    {
        boards.MapPost("/{boardId}/cards", AddCardAsync).WithName("addCard");
        boards.MapGet("/{boardId}/cards/{cardId}", OpenCardAsync).WithName("openCard");
        boards.MapPatch("/{boardId}/cards/{cardId}", EditCardAsync).WithName("editCard");
        boards.MapDelete("/{boardId}/cards/{cardId}", DeleteCardAsync).WithName("deleteCard");

        return boards;
    }

    private static async Task AddCardAsync(
        HttpContext context,
        AddCard add,
        OpenBoard open,
        string boardId,
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        if (RecognisedSession.AccountId(context.User) is not { } accountId)
        {
            await context.WriteAccountProblemAsync("accounts.session_not_recognised");
            return;
        }

        if (!Guid.TryParse(boardId, out var id))
        {
            await context.WriteBoardProblemAsync(BoardErrors.NotAvailable);
            return;
        }

        var request = new BoardRequestBody(body);
        if (!request.TryGetString("column_id", out var columnText)
            || !request.TryGetString("title", out var title)
            || !TryGetOptionalString(body, "description", out var description))
        {
            await RefuseShapeAsync(context, open, id, accountId, cancellationToken);
            return;
        }

        // A column_id that is not a UUID names no column on this board. Guid.Empty is never an id
        // (Ids.New() issues v7 only), so the Board answers it as any absent column, after membership.
        var columnId = Guid.TryParse(columnText, out var parsed) ? parsed : Guid.Empty;

        var result = await add.ExecuteAsync(
            id, accountId, columnId, title, description ?? string.Empty, cancellationToken);
        if (!result.IsSuccess)
        {
            await context.WriteBoardProblemAsync(result.Error!);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status201Created;
        await context.Response.WriteAsJsonAsync(result.Value, Json, cancellationToken);
    }

    private static async Task OpenCardAsync(
        HttpContext context,
        OpenCard open,
        string boardId,
        string cardId,
        CancellationToken cancellationToken)
    {
        if (RecognisedSession.AccountId(context.User) is not { } accountId)
        {
            await context.WriteAccountProblemAsync("accounts.session_not_recognised");
            return;
        }

        if (!Guid.TryParse(boardId, out var id) || !Guid.TryParse(cardId, out var card))
        {
            await context.WriteBoardProblemAsync(BoardErrors.NotAvailable);
            return;
        }

        var result = await open.ExecuteAsync(id, accountId, card, cancellationToken);
        if (!result.IsSuccess)
        {
            await context.WriteBoardProblemAsync(result.Error!);
            return;
        }

        await context.Response.WriteAsJsonAsync(result.Value, Json, cancellationToken);
    }

    private static async Task EditCardAsync(
        HttpContext context,
        EditCard edit,
        OpenBoard open,
        OpenCard openCard,
        string boardId,
        string cardId,
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        if (RecognisedSession.AccountId(context.User) is not { } accountId)
        {
            await context.WriteAccountProblemAsync("accounts.session_not_recognised");
            return;
        }

        if (!Guid.TryParse(boardId, out var id) || !Guid.TryParse(cardId, out var card))
        {
            await context.WriteBoardProblemAsync(BoardErrors.NotAvailable);
            return;
        }

        // Neither title nor description is the same incomplete request as a field of the wrong type.
        if (!new BoardRequestBody(body).TryGetInt32("content_version", out var contentVersion)
            || !TryGetOptionalString(body, "title", out var title)
            || !TryGetOptionalString(body, "description", out var description)
            || (title is null && description is null))
        {
            await RefuseShapeAsync(context, open, id, accountId, cancellationToken);
            return;
        }

        var result = await edit.ExecuteAsync(
            id, accountId, card, title, description, contentVersion, cancellationToken);
        if (!result.IsSuccess)
        {
            await WriteCardRefusalAsync(context, openCard, id, accountId, card, result.Error!, cancellationToken);
            return;
        }

        await context.Response.WriteAsJsonAsync(result.Value, Json, cancellationToken);
    }

    private static async Task DeleteCardAsync(
        HttpContext context,
        DeleteCard delete,
        OpenBoard open,
        OpenCard openCard,
        string boardId,
        string cardId,
        [FromBody] JsonElement? body,
        CancellationToken cancellationToken)
    {
        if (RecognisedSession.AccountId(context.User) is not { } accountId)
        {
            await context.WriteAccountProblemAsync("accounts.session_not_recognised");
            return;
        }

        if (!Guid.TryParse(boardId, out var id) || !Guid.TryParse(cardId, out var card))
        {
            await context.WriteBoardProblemAsync(BoardErrors.NotAvailable);
            return;
        }

        // A DELETE may arrive with no body at all; that is the same incomplete request as one
        // missing content_version, answered only after the membership check.
        if (!new BoardRequestBody(body ?? default).TryGetInt32("content_version", out var contentVersion))
        {
            await RefuseShapeAsync(context, open, id, accountId, cancellationToken);
            return;
        }

        var result = await delete.ExecuteAsync(id, accountId, card, contentVersion, cancellationToken);
        if (!result.IsSuccess)
        {
            await WriteCardRefusalAsync(context, openCard, id, accountId, card, result.Error!, cancellationToken);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    /// <summary>
    /// A card change refused: <c>boards.card_changed</c> carries the card as it now stands (AC-23),
    /// read through the member-scoped <see cref="OpenCard"/> — a board, membership or card gone since
    /// is answered <c>boards.not_available</c>; every other refusal is written as it came.
    /// </summary>
    private static async Task WriteCardRefusalAsync(
        HttpContext context,
        OpenCard openCard,
        Guid boardId,
        Guid accountId,
        Guid cardId,
        BoardError error,
        CancellationToken cancellationToken)
    {
        if (error.Code != BoardProblems.CardChanged)
        {
            await context.WriteBoardProblemAsync(error);
            return;
        }

        var current = await openCard.ExecuteAsync(boardId, accountId, cardId, cancellationToken);
        if (!current.IsSuccess)
        {
            await context.WriteBoardProblemAsync(current.Error!);
            return;
        }

        await context.WriteBoardProblemAsync(BoardProblems.CardChanged, current.Value);
    }

    /// <summary>
    /// An optional string member: absent is <see langword="null"/> and fine; present, it must be a
    /// string, or the request is the contract's <c>boards.request_invalid</c>.
    /// </summary>
    private static bool TryGetOptionalString(JsonElement body, string name, out string? value)
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

        if (!new BoardRequestBody(body).TryGetString(name, out var present))
        {
            return false;
        }

        value = present;
        return true;
    }
}
