using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Boards;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Api.IntegrationTests.Boards;

/// <summary>
/// T8 — the card use cases resolved from <see cref="ApiFactory"/> services, the way
/// <c>ColumnUseCaseTests</c> does, one row of the task's Definition of Done per test. Every one of
/// the four begins with the member-scoped load (ADR 0014): the "card from another board" and
/// "deleted card" cases below are what proves that from outside (AC-18b, AC-26).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CardUseCaseTests(ApiFactory factory)
{
    // ---- AC-12: a card is added at the end of its column, its title shown -------------------------

    [Fact]
    public async Task Adding_a_card_places_it_last_in_its_column()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A board with room for cards");
        var columnId = await FirstColumnIdAsync(boardId);
        await InsertCardAsync(boardId, columnId, position: 0);

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<AddCard>()
            .ExecuteAsync(boardId, owner.Id, columnId, "A new card", "Some detail", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("A new card", result.Value.Title);
        Assert.Equal(1, result.Value.Position);
        Assert.Equal(2, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Cards] WHERE [ColumnId] = '{columnId}'"));
        Assert.Equal(2, await factory.ScalarAsync<int>(
            $"SELECT [CardCount] FROM [dbo].[Boards] WHERE [Id] = '{boardId}'"));
    }

    // ---- AC-15 edge case: two adds forced to collide at 999 cards let only one land ----------------

    [Fact]
    public async Task Two_adds_forced_to_collide_at_nine_hundred_ninety_nine_cards_leave_exactly_one_thousand()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A nearly full board");
        var columnId = await FirstColumnIdAsync(boardId);
        await FillCardsUpToAsync(boardId, columnId, Board.MaxCards - 1);

        async Task<Domain.Result<CardSummary, BoardError>> AddAsync(string title)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AddCard>()
                .ExecuteAsync(boardId, owner.Id, columnId, title, null, CancellationToken.None);
        }

        // Forced to collide: the first save is held until the other racer has committed, so the
        // held one must lose its first attempt and re-decide on reload (review Q2a).
        var race = await factory.Contention.RaceAsync(() => AddAsync("Racer A"), () => AddAsync("Racer B"));
        Assert.True(race.Collided, "the two adds never collided");
        var results = new[] { race.First, race.Second };

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        var refusal = results.Single(r => !r.IsSuccess);
        Assert.Same(BoardErrors.CardLimitReached, refusal.Error);
        Assert.Equal(Board.MaxCards, await factory.ScalarAsync<int>(
            $"SELECT [CardCount] FROM [dbo].[Boards] WHERE [Id] = '{boardId}'"));
    }

    // ---- AC-13: an edit records the new title and description ---------------------------------------

    [Fact]
    public async Task Editing_a_card_records_its_new_title_and_description()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A board to edit a card on");
        var columnId = await FirstColumnIdAsync(boardId);
        var cardId = await InsertCardAsync(boardId, columnId, position: 0);

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<EditCard>()
            .ExecuteAsync(boardId, owner.Id, cardId, "Renamed card", "New detail", seenContentVersion: 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed card", result.Value.Title);
        Assert.Equal("New detail", result.Value.Description);
        Assert.Equal("Renamed card", await factory.ScalarAsync<string>(
            $"SELECT [Title] FROM [dbo].[Cards] WHERE [Id] = '{cardId}'"));
    }

    // ---- AC-23: two simultaneous edits of one card let only one land --------------------------------

    [Fact]
    public async Task Two_edits_of_one_card_at_once_let_only_one_land_and_the_loser_sees_the_winner()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A board edited twice at once");
        var columnId = await FirstColumnIdAsync(boardId);
        var cardId = await InsertCardAsync(boardId, columnId, position: 0);

        async Task<Domain.Result<CardView, BoardError>> EditAsync(string title)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<EditCard>()
                .ExecuteAsync(boardId, owner.Id, cardId, title, null, seenContentVersion: 1, CancellationToken.None);
        }

        // Forced to collide: the first save is held until the other racer has committed, so the
        // held one must lose its first attempt and re-decide on reload (review Q2a).
        var race = await factory.Contention.RaceAsync(() => EditAsync("Racer A"), () => EditAsync("Racer B"));
        Assert.True(race.Collided, "the two edits never collided");
        var results = new[] { race.First, race.Second };

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        var winnerTitle = await factory.ScalarAsync<string>(
            $"SELECT [Title] FROM [dbo].[Cards] WHERE [Id] = '{cardId}'");
        Assert.True(winnerTitle is "Racer A" or "Racer B");

        var refusal = results.Single(r => !r.IsSuccess);
        Assert.Equal("boards.card_changed", refusal.Error!.Code);
        Assert.Equal(winnerTitle, refusal.Error.CurrentCard!.Title);
    }

    // ---- AC-18: deleting a card removes it and leaves the others' positions untouched ----------------

    [Fact]
    public async Task Deleting_a_card_removes_it_and_leaves_the_others_positions_untouched()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A board to delete a card on");
        var columnId = await FirstColumnIdAsync(boardId);
        var firstCardId = await InsertCardAsync(boardId, columnId, position: 0);
        var secondCardId = await InsertCardAsync(boardId, columnId, position: 1);
        var thirdCardId = await InsertCardAsync(boardId, columnId, position: 2);
        await SetColumnCountersAsync(columnId, cardCount: 3, nextCardPosition: 3);
        await SetBoardCardCountAsync(boardId, 3);

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DeleteCard>()
            .ExecuteAsync(boardId, owner.Id, secondCardId, seenContentVersion: 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Cards] WHERE [ColumnId] = '{columnId}'"));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT [Position] FROM [dbo].[Cards] WHERE [Id] = '{firstCardId}'"));
        Assert.Equal(2, await factory.ScalarAsync<int>(
            $"SELECT [Position] FROM [dbo].[Cards] WHERE [Id] = '{thirdCardId}'"));

        // ---- AC-18: a member who then tries to change the deleted card is refused as never-existed --
        var afterDelete = await scope.ServiceProvider.GetRequiredService<EditCard>()
            .ExecuteAsync(boardId, owner.Id, secondCardId, "Too late", null, 1, CancellationToken.None);
        Assert.Same(BoardErrors.NotAvailable, afterDelete.Error);
    }

    // ---- AC-18b / AC-26: a card id from another board answers NotAvailable, nothing changed --------

    [Fact]
    public async Task A_card_id_from_another_board_the_caller_belongs_to_answers_not_available()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "This board");
        var otherBoardId = await CreateBoardAsync(owner.Id, "Another board of the same owner");
        var otherColumnId = await FirstColumnIdAsync(otherBoardId);
        var foreignCardId = await InsertCardAsync(otherBoardId, otherColumnId, position: 0);

        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        var opened = await services.GetRequiredService<OpenCard>()
            .ExecuteAsync(boardId, owner.Id, foreignCardId, CancellationToken.None);
        var edited = await services.GetRequiredService<EditCard>()
            .ExecuteAsync(boardId, owner.Id, foreignCardId, "Hijacked", null, 1, CancellationToken.None);
        var deleted = await services.GetRequiredService<DeleteCard>()
            .ExecuteAsync(boardId, owner.Id, foreignCardId, 1, CancellationToken.None);

        Assert.Same(BoardErrors.NotAvailable, opened.Error);
        Assert.Same(BoardErrors.NotAvailable, edited.Error);
        Assert.Same(BoardErrors.NotAvailable, deleted.Error);
        Assert.Equal("A card", await factory.ScalarAsync<string>(
            $"SELECT [Title] FROM [dbo].[Cards] WHERE [Id] = '{foreignCardId}'"));
    }

    // ---- Every one of the four begins with the member-scoped load: a non-member is refused ---------

    [Fact]
    public async Task A_non_member_is_refused_not_available_by_every_one_of_the_four()
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "Not the stranger's board");
        var columnId = await FirstColumnIdAsync(boardId);
        var cardId = await InsertCardAsync(boardId, columnId, position: 0);

        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        var added = await services.GetRequiredService<AddCard>()
            .ExecuteAsync(boardId, stranger.Id, columnId, "Sneaky", null, CancellationToken.None);
        var opened = await services.GetRequiredService<OpenCard>()
            .ExecuteAsync(boardId, stranger.Id, cardId, CancellationToken.None);
        var edited = await services.GetRequiredService<EditCard>()
            .ExecuteAsync(boardId, stranger.Id, cardId, "Sneaky", null, 1, CancellationToken.None);
        var deleted = await services.GetRequiredService<DeleteCard>()
            .ExecuteAsync(boardId, stranger.Id, cardId, 1, CancellationToken.None);

        Assert.Same(BoardErrors.NotAvailable, added.Error);
        Assert.Same(BoardErrors.NotAvailable, opened.Error);
        Assert.Same(BoardErrors.NotAvailable, edited.Error);
        Assert.Same(BoardErrors.NotAvailable, deleted.Error);
        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Cards] WHERE [BoardId] = '{boardId}'"));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private async Task<Guid> CreateBoardAsync(Guid ownerId, string name)
    {
        using var scope = factory.Services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(ownerId, name, CancellationToken.None);
        Assert.True(created.IsSuccess);
        return created.Value.Id;
    }

    private async Task<Guid> FirstColumnIdAsync(Guid boardId) =>
        await factory.ScalarAsync<Guid>(
            $"SELECT TOP 1 [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' ORDER BY [Position]");

    /// <summary>Inserts one card row directly, bumping the column's and board's counters to match.</summary>
    private async Task<Guid> InsertCardAsync(Guid boardId, Guid columnId, int position)
    {
        var cardId = Guid.CreateVersion7();
        await factory.ExecuteAsync(
            $"""
            INSERT INTO [dbo].[Cards]
                ([Id], [BoardId], [ColumnId], [Position], [Title], [Description], [ContentVersion])
            VALUES ('{cardId}', '{boardId}', '{columnId}', {position}, N'A card', N'', 1);
            """);
        await factory.ExecuteAsync(
            $"""
            UPDATE [dbo].[Columns]
            SET [CardCount] = [CardCount] + 1, [NextCardPosition] = {position} + 1
            WHERE [Id] = '{columnId}';
            """);
        await factory.ExecuteAsync(
            $"UPDATE [dbo].[Boards] SET [CardCount] = [CardCount] + 1 WHERE [Id] = '{boardId}';");
        return cardId;
    }

    private Task SetColumnCountersAsync(Guid columnId, int cardCount, int nextCardPosition) =>
        factory.ExecuteAsync(
            $"""
            UPDATE [dbo].[Columns] SET [CardCount] = {cardCount}, [NextCardPosition] = {nextCardPosition}
            WHERE [Id] = '{columnId}';
            """);

    private Task SetBoardCardCountAsync(Guid boardId, int cardCount) =>
        factory.ExecuteAsync(
            $"UPDATE [dbo].[Boards] SET [CardCount] = {cardCount} WHERE [Id] = '{boardId}';");

    /// <summary>Fills a column (and the board's counter) up to <paramref name="total"/> cards.</summary>
    private async Task FillCardsUpToAsync(Guid boardId, Guid columnId, int total)
    {
        for (var position = 0; position < total; position++)
        {
            await factory.ExecuteAsync(
                $"""
                INSERT INTO [dbo].[Cards]
                    ([Id], [BoardId], [ColumnId], [Position], [Title], [Description], [ContentVersion])
                VALUES ('{Guid.CreateVersion7()}', '{boardId}', '{columnId}', {position}, N'Filler {position}', N'', 1);
                """);
        }

        await SetColumnCountersAsync(columnId, cardCount: total, nextCardPosition: total);
        await SetBoardCardCountAsync(boardId, total);
    }
}
