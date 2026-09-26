using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Application.Boards;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Api.Boards;

/// <summary>
/// The column endpoints on the board group: <c>addColumn</c>, <c>renameColumn</c>,
/// <c>moveColumn</c> and <c>deleteColumn</c> (contracts/openapi.yaml, paths
/// <c>/api/v1/boards/{boardId}/columns*</c>). The version counter each change is checked against
/// travels in the body (ADR 0016), read leniently like every other board body (ADR 0014).
/// </summary>
/// <remarks>
/// A stale change is answered with the thing that changed as it now stands —
/// <c>current_column</c> on <c>boards.column_renamed</c>, <c>current_layout</c> on
/// <c>boards.columns_changed</c> — read afterwards through the same member-scoped load, so it only
/// ever reaches a member, and a board or column gone in the meantime is answered as any absent one
/// is (<see cref="WriteStaleAsync"/>).
/// </remarks>
public static partial class BoardEndpoints
{
    private static RouteGroupBuilder MapColumnEndpoints(this RouteGroupBuilder boards)
    {
        boards.MapPost("/{boardId}/columns", AddColumnAsync).WithName("addColumn");
        boards.MapPatch("/{boardId}/columns/{columnId}", RenameColumnAsync).WithName("renameColumn");
        boards.MapPut("/{boardId}/columns/{columnId}/position", MoveColumnAsync).WithName("moveColumn");
        boards.MapDelete("/{boardId}/columns/{columnId}", DeleteColumnAsync).WithName("deleteColumn");

        return boards;
    }

    private static async Task AddColumnAsync(
        HttpContext context,
        AddColumn add,
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
        if (!request.TryGetString("name", out var name) || !request.HasOnly("name"))
        {
            await RefuseShapeAsync(context, open, id, accountId, cancellationToken);
            return;
        }

        var result = await add.ExecuteAsync(id, accountId, name, cancellationToken);
        if (!result.IsSuccess)
        {
            await context.WriteBoardProblemAsync(result.Error!);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status201Created;
        await context.Response.WriteAsJsonAsync(result.Value, Json, cancellationToken);
    }

    private static async Task RenameColumnAsync(
        HttpContext context,
        RenameColumn rename,
        OpenBoard open,
        string boardId,
        string columnId,
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

        var column = BoardRequestBody.ItemId(columnId);

        var request = new BoardRequestBody(body);
        if (!request.TryGetString("name", out var name)
            || !request.TryGetInt32("name_version", out var nameVersion)
            || !request.HasOnly("name", "name_version"))
        {
            await RefuseShapeAsync(context, open, id, accountId, cancellationToken);
            return;
        }

        var result = await rename.ExecuteAsync(id, accountId, column, name, nameVersion, cancellationToken);
        if (!result.IsSuccess)
        {
            await WriteColumnRefusalAsync(context, open, id, accountId, column, result.Error!, cancellationToken);
            return;
        }

        await context.Response.WriteAsJsonAsync(result.Value, Json, cancellationToken);
    }

    private static async Task MoveColumnAsync(
        HttpContext context,
        MoveColumn move,
        OpenBoard open,
        string boardId,
        string columnId,
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

        var column = BoardRequestBody.ItemId(columnId);

        var request = new BoardRequestBody(body);
        if (!request.TryGetInt32("position", out var position)
            || !request.TryGetInt32("column_layout_version", out var layoutVersion)
            || !request.HasOnly("position", "column_layout_version"))
        {
            await RefuseShapeAsync(context, open, id, accountId, cancellationToken);
            return;
        }

        var result = await move.ExecuteAsync(id, accountId, column, position, layoutVersion, cancellationToken);
        if (!result.IsSuccess)
        {
            await WriteColumnRefusalAsync(context, open, id, accountId, column, result.Error!, cancellationToken);
            return;
        }

        await context.Response.WriteAsJsonAsync(result.Value, Json, cancellationToken);
    }

    private static async Task DeleteColumnAsync(
        HttpContext context,
        DeleteColumn delete,
        OpenBoard open,
        string boardId,
        string columnId,
        [FromBody] JsonElement? body,
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

        var column = BoardRequestBody.ItemId(columnId);

        // A DELETE may arrive with no body at all; that is the same incomplete request as one
        // missing name_version, answered only after the membership check.
        var request = new BoardRequestBody(body ?? default);
        if (!request.TryGetInt32("name_version", out var nameVersion) || !request.HasOnly("name_version"))
        {
            await RefuseShapeAsync(context, open, id, accountId, cancellationToken);
            return;
        }

        var result = await delete.ExecuteAsync(id, accountId, column, nameVersion, cancellationToken);
        if (!result.IsSuccess)
        {
            await WriteColumnRefusalAsync(context, open, id, accountId, column, result.Error!, cancellationToken);
            return;
        }

        await context.Response.WriteAsJsonAsync(result.Value, Json, cancellationToken);
    }

    /// <summary>
    /// A column change refused: the two stale refusals carry the column (AC-06b) or the layout
    /// (AC-24) as it now stands; every other refusal is written as it came.
    /// </summary>
    private static Task WriteColumnRefusalAsync(
        HttpContext context,
        OpenBoard open,
        Guid boardId,
        Guid accountId,
        Guid columnId,
        BoardError error,
        CancellationToken cancellationToken) => error.Code switch
        {
            BoardProblems.ColumnRenamed => WriteStaleAsync(
                context, open, boardId, accountId, error.Code,
                board => board.Columns.SingleOrDefault(c => c.Id == columnId),
                cancellationToken),
            BoardProblems.ColumnsChanged => WriteStaleAsync(
                context, open, boardId, accountId, error.Code,
                board => new ColumnLayout(board.ColumnLayoutVersion, board.Columns),
                cancellationToken),
            _ => context.WriteBoardProblemAsync(error),
        };

    /// <summary>
    /// Writes a stale refusal with <paramref name="current"/> read from the board as it now stands,
    /// through the member-scoped load — a board, membership or column gone since the refusal is
    /// answered <c>boards.not_available</c>, as any absent one is.
    /// </summary>
    private static async Task WriteStaleAsync(
        HttpContext context,
        OpenBoard open,
        Guid boardId,
        Guid accountId,
        string code,
        Func<BoardOutline, object?> current,
        CancellationToken cancellationToken)
    {
        // The board's columns as they now stand, without its cards (review Q4f).
        var board = await open.OutlineAsync(boardId, accountId, cancellationToken);
        if (!board.IsSuccess)
        {
            await context.WriteBoardProblemAsync(board.Error!);
            return;
        }

        if (current(board.Value) is not { } now)
        {
            await context.WriteBoardProblemAsync(BoardErrors.NotAvailable);
            return;
        }

        await context.WriteBoardProblemAsync(code, now);
    }
}
