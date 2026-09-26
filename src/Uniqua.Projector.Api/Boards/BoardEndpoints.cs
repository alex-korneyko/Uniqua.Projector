using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Application.Boards;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Api.Boards;

/// <summary>
/// The <c>/api/v1/boards</c> route group: <c>listMyBoards</c>, <c>createBoard</c>, <c>openBoard</c>,
/// <c>renameBoard</c> and <c>deleteBoard</c>, and the column and card endpoints under it
/// (<see cref="MapColumnEndpoints"/>, BoardEndpoints.Columns.cs; <see cref="MapCardEndpoints"/>,
/// BoardEndpoints.Cards.cs). Each endpoint translates — it reads the request, calls
/// a use case and turns the outcome into the contract's response. None decides what is legal and
/// none builds an error body: every refusal goes through <see cref="ProblemDetailsSetup"/> worded by
/// <see cref="BoardProblems"/> (sad.md §8).
/// </summary>
/// <remarks>
/// <para>
/// Binding is lenient (ADR 0014). A path id is taken as any string — one that is not a UUID is just a
/// board that does not exist — and a body as any JSON (<see cref="BoardRequestBody"/>), so the
/// framework can refuse nothing before the membership check except a body that is not JSON at all.
/// </para>
/// <para>
/// Order of checks (contracts/openapi.yaml, info.description): … → member-scoped board load →
/// request shape → owner check → text limits. A body of the wrong shape is therefore answered only
/// after the caller has been shown to be a member (<see cref="RefuseShapeAsync"/>), and before the
/// owner check the use case would otherwise run.
/// </para>
/// </remarks>
public static partial class BoardEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    /// <summary>
    /// The contract's member names are snake_case (contracts/openapi.yaml, components.schemas); the
    /// board views are written with this policy rather than restating every name as an attribute.
    /// </summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static IEndpointRouteBuilder MapBoardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The change limit filters the whole group, so it runs after the session is recognised and
        // before any endpoint's membership check; it lets every read through uncounted (AC-17).
        var boards = endpoints.MapGroup("/api/v1/boards").RequireAuthorization();
        boards.AddEndpointFilter((context, next) => context.HttpContext.RequestServices
            .GetRequiredService<BoardChangeRateLimit>()
            .InvokeAsync(context, next));

        boards.MapGet("", ListMyBoardsAsync).WithName("listMyBoards");
        boards.MapPost("", CreateBoardAsync).WithName("createBoard");
        boards.MapGet("/{boardId}", OpenBoardAsync).WithName("openBoard");
        boards.MapPatch("/{boardId}", RenameBoardAsync).WithName("renameBoard");
        boards.MapDelete("/{boardId}", DeleteBoardAsync).WithName("deleteBoard");
        boards.MapColumnEndpoints();
        boards.MapCardEndpoints();

        return endpoints;
    }

    private static async Task ListMyBoardsAsync(
        HttpContext context,
        ListMyBoards list,
        [FromQuery] string? after,
        [FromQuery] string? before,
        [FromQuery] string? limit,
        CancellationToken cancellationToken)
    {
        if (RecognisedSession.AccountId(context.User) is not { } accountId)
        {
            await context.WriteAccountProblemAsync("accounts.session_not_recognised");
            return;
        }

        var entries = await list.ExecuteAsync(accountId, cancellationToken);

        if (!BoardListPaging.TryPage(entries, after, before, limit, out var page))
        {
            await context.WriteBoardProblemAsync(BoardProblems.RequestInvalid);
            return;
        }

        await context.Response.WriteAsJsonAsync(page, Json, cancellationToken);
    }

    private static async Task CreateBoardAsync(
        HttpContext context,
        CreateBoard create,
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        if (RecognisedSession.AccountId(context.User) is not { } accountId)
        {
            await context.WriteAccountProblemAsync("accounts.session_not_recognised");
            return;
        }

        // No board is named yet, so there is no membership to establish before judging the shape.
        var request = new BoardRequestBody(body);
        if (!request.TryGetString("name", out var name) || !request.HasOnly("name"))
        {
            await context.WriteBoardProblemAsync(BoardProblems.RequestInvalid);
            return;
        }

        var result = await create.ExecuteAsync(accountId, name, cancellationToken);
        if (!result.IsSuccess)
        {
            await context.WriteBoardProblemAsync(result.Error!);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status201Created;
        await context.Response.WriteAsJsonAsync(result.Value, Json, cancellationToken);
    }

    private static async Task OpenBoardAsync(
        HttpContext context,
        OpenBoard open,
        string boardId,
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

        var result = await open.ExecuteAsync(id, accountId, cancellationToken);
        if (!result.IsSuccess)
        {
            await context.WriteBoardProblemAsync(result.Error!);
            return;
        }

        await context.Response.WriteAsJsonAsync(result.Value, Json, cancellationToken);
    }

    private static async Task RenameBoardAsync(
        HttpContext context,
        RenameBoard rename,
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

        var result = await rename.ExecuteAsync(id, accountId, name, cancellationToken);
        if (!result.IsSuccess)
        {
            await context.WriteBoardProblemAsync(result.Error!);
            return;
        }

        // The use case stored the name trimmed by the Text rule; that is the name the board now has.
        await context.Response.WriteAsJsonAsync(
            new BoardName(id, BoardText.Trim(name)), Json, cancellationToken);
    }

    private static async Task DeleteBoardAsync(
        HttpContext context,
        DeleteBoard delete,
        OpenBoard open,
        string boardId,
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

        // The confirmation travels in the body only; a confirm_name in the query string is never
        // read, since URLs reach proxy access logs and board names are never logged (sad.md §8).
        // A DELETE may arrive with no body at all; that is the same incomplete request as one
        // missing confirm_name, answered only after the membership check, as deleteColumn and
        // deleteCard answer it (review Q4d).
        var request = new BoardRequestBody(body ?? default);
        if (!request.TryGetString("confirm_name", out var typedName) || !request.HasOnly("confirm_name"))
        {
            await RefuseShapeAsync(context, open, id, accountId, cancellationToken);
            return;
        }

        var result = await delete.ExecuteAsync(id, accountId, typedName, cancellationToken);
        if (result.IsSuccess)
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        if (result.Error!.Code != BoardProblems.ConfirmationMismatch)
        {
            await context.WriteBoardProblemAsync(result.Error);
            return;
        }

        // AC-20b: shown the board's current name — read through the same member-scoped load, so a
        // board deleted in the meantime is answered as every absent board is.
        var current = await open.ExecuteAsync(id, accountId, cancellationToken);
        if (!current.IsSuccess)
        {
            await context.WriteBoardProblemAsync(current.Error!);
            return;
        }

        await context.WriteBoardProblemAsync(BoardProblems.ConfirmationMismatch, current.Value.Name);
    }

    /// <summary>
    /// A body of the wrong shape, answered in the contract's order: the member-scoped load first, so
    /// a non-member (or an absent board) gets the one <c>boards.not_available</c> and only a member
    /// learns that the request itself was incomplete (AC-25, ADR 0014).
    /// </summary>
    private static async Task RefuseShapeAsync(
        HttpContext context, OpenBoard open, Guid boardId, Guid accountId, CancellationToken cancellationToken)
    {
        var member = await open.ExecuteAsync(boardId, accountId, cancellationToken);

        await context.WriteBoardProblemAsync(
            member.IsSuccess ? BoardProblems.RequestInvalid : member.Error!.Code);
    }

    /// <summary>
    /// <c>listMyBoards</c>' cursor wrapper (contracts/openapi.yaml, components.schemas.BoardListPage).
    /// Today one page holds the whole list (an account owns at most 50 boards and nobody joins
    /// another's before invitations), so the page is cut from the full list in memory; the cursor is
    /// the opaque id of the entry it continues from.
    /// </summary>
    private static class BoardListPaging
    {
        public static bool TryPage(
            IReadOnlyList<BoardListEntry> all,
            string? after,
            string? before,
            string? limitText,
            out BoardListPage page)
        {
            page = default!;

            var limit = DefaultPageSize;
            if (limitText is not null
                && (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                    || limit is < 1 or > MaxPageSize))
            {
                return false;
            }

            if (after is not null && before is not null)
            {
                return false;
            }

            int start;
            int end;
            if (after is not null)
            {
                if (IndexOf(all, after) is not { } index)
                {
                    return false;
                }

                start = index + 1;
                end = Math.Min(start + limit, all.Count);
            }
            else if (before is not null)
            {
                if (IndexOf(all, before) is not { } index)
                {
                    return false;
                }

                end = index;
                start = Math.Max(0, end - limit);
            }
            else
            {
                start = 0;
                end = Math.Min(limit, all.Count);
            }

            var items = all.Skip(start).Take(end - start).ToList();
            var hasPrev = items.Count > 0 && start > 0;
            var hasNext = items.Count > 0 && end < all.Count;

            page = new BoardListPage(
                items,
                hasNext,
                hasPrev,
                hasNext ? Cursor(items[^1]) : null,
                hasPrev ? Cursor(items[0]) : null);
            return true;
        }

        private static string Cursor(BoardListEntry entry) => entry.Id.ToString("N");

        private static int? IndexOf(IReadOnlyList<BoardListEntry> all, string cursor)
        {
            if (!Guid.TryParseExact(cursor, "N", out var id))
            {
                return null;
            }

            for (var index = 0; index < all.Count; index++)
            {
                if (all[index].Id == id)
                {
                    return index;
                }
            }

            return null;
        }
    }
}

/// <summary>contracts/openapi.yaml, components.schemas.BoardListPage.</summary>
public sealed record BoardListPage(
    IReadOnlyList<BoardListEntry> Items,
    bool HasNext,
    bool HasPrev,
    string? NextCursor,
    string? PrevCursor);

/// <summary>contracts/openapi.yaml, components.schemas.BoardName — what a rename answers.</summary>
public sealed record BoardName(Guid Id, string Name);
