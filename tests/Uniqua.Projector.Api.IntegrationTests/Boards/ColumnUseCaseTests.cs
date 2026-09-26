using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Boards;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Api.IntegrationTests.Boards;

/// <summary>
/// T7 — the column use cases resolved from <see cref="ApiFactory"/> services, the way
/// <c>BoardUseCaseTests</c> does, one row of the task's Definition of Done per test. Every one of
/// the four begins with the member-scoped load (ADR 0014): the "column from another board" and
/// "deleted column" cases below are what proves that from outside. No role is checked anywhere in
/// these four (AC-21): every happy-path test below is run once as a non-owner <c>Member</c>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ColumnUseCaseTests(ApiFactory factory)
{
    // ---- AC-05 / AC-21: a Member adds a column exactly as the Owner could -------------------------

    [Fact]
    public async Task A_member_adds_a_column_at_the_end_exactly_as_the_owner_could()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A board with room");
        await InsertMembershipAsync(boardId, member.Id, "Member");

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<AddColumn>()
            .ExecuteAsync(boardId, member.Id, "Blocked", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Blocked", result.Value.Column.Name);
        Assert.Equal(3, result.Value.Column.Position);
        Assert.Equal(4, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'"));
    }

    // ---- AC-11: a board that already has 20 columns refuses a 21st --------------------------------

    [Fact]
    public async Task A_board_already_holding_twenty_columns_refuses_a_twenty_first()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A full board");
        await FillColumnsUpToAsync(boardId, Board.MaxColumns);

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<AddColumn>()
            .ExecuteAsync(boardId, owner.Id, "One too many", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.ColumnLimitReached, result.Error);
        Assert.Equal(Board.MaxColumns, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'"));
    }

    // ---- AC-11 edge case: a forced-colliding 20th/21st add leaves exactly 20 ------------------------

    [Fact]
    public async Task Two_adds_forced_to_collide_at_nineteen_columns_leave_exactly_twenty()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "Nearly full");
        await FillColumnsUpToAsync(boardId, Board.MaxColumns - 1);

        async Task<Domain.Result<AddedColumn, BoardError>> AddAsync(string name)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AddColumn>()
                .ExecuteAsync(boardId, owner.Id, name, CancellationToken.None);
        }

        var results = await Task.WhenAll(AddAsync("Racer A"), AddAsync("Racer B"));

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        var refusal = results.Single(r => !r.IsSuccess);
        Assert.Same(BoardErrors.ColumnLimitReached, refusal.Error);
        Assert.Equal(Board.MaxColumns, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'"));
    }

    // ---- AC-06: rename records the new name, move records the new order ---------------------------

    [Fact]
    public async Task Renaming_a_column_records_its_new_name()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A board to rename on");
        var columnId = await FirstColumnIdAsync(boardId);

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RenameColumn>()
            .ExecuteAsync(boardId, owner.Id, columnId, "Backlog", seenNameVersion: 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Backlog", result.Value.Name);
        Assert.Equal("Backlog", await factory.ScalarAsync<string>(
            $"SELECT [Name] FROM [dbo].[Columns] WHERE [Id] = '{columnId}'"));
    }

    [Fact]
    public async Task Moving_a_column_records_its_new_position_for_every_member()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A board to reorder");
        var columnId = await FirstColumnIdAsync(boardId);

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<MoveColumn>()
            .ExecuteAsync(boardId, owner.Id, columnId, position: 2, seenLayoutVersion: 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Columns.Single(c => c.Id == columnId).Position);
        Assert.Equal(2, await factory.ScalarAsync<int>(
            $"SELECT [Position] FROM [dbo].[Columns] WHERE [Id] = '{columnId}'"));
    }

    // ---- AC-06b: a rename or delete against a stale name version is refused, unchanged ------------

    [Fact]
    public async Task Renaming_against_a_stale_name_version_is_refused_and_names_the_current_name()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A board renamed under them");
        var columnId = await FirstColumnIdAsync(boardId);

        using (var firstScope = factory.Services.CreateScope())
        {
            var renamed = await firstScope.ServiceProvider.GetRequiredService<RenameColumn>()
                .ExecuteAsync(boardId, owner.Id, columnId, "Renamed already", 1, CancellationToken.None);
            Assert.True(renamed.IsSuccess);
        }

        using var scope = factory.Services.CreateScope();
        var stale = await scope.ServiceProvider.GetRequiredService<RenameColumn>()
            .ExecuteAsync(boardId, owner.Id, columnId, "My own new name", 1, CancellationToken.None);

        Assert.False(stale.IsSuccess);
        Assert.Equal("boards.column_renamed", stale.Error!.Code);
        Assert.Equal("Renamed already", stale.Error.CurrentColumn!.Name);
        Assert.Equal("Renamed already", await factory.ScalarAsync<string>(
            $"SELECT [Name] FROM [dbo].[Columns] WHERE [Id] = '{columnId}'"));
    }

    [Fact]
    public async Task Deleting_against_a_stale_name_version_is_refused_and_the_column_survives()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "Another board renamed under them");
        var columnId = await FirstColumnIdAsync(boardId);

        using (var firstScope = factory.Services.CreateScope())
        {
            var renamed = await firstScope.ServiceProvider.GetRequiredService<RenameColumn>()
                .ExecuteAsync(boardId, owner.Id, columnId, "Renamed since", 1, CancellationToken.None);
            Assert.True(renamed.IsSuccess);
        }

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DeleteColumn>()
            .ExecuteAsync(boardId, owner.Id, columnId, seenNameVersion: 1, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("boards.column_renamed", result.Error!.Code);
        Assert.Equal("Renamed since", result.Error.CurrentColumn!.Name);
        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [Id] = '{columnId}'"));
    }

    // ---- AC-07: deleting an empty column removes it and keeps the others' relative order ----------

    [Fact]
    public async Task Deleting_an_empty_column_removes_it_and_keeps_the_others_relative_order()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A board with a spare column");
        var (firstId, _, lastId) = await ThreeDefaultColumnIdsAsync(boardId);

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DeleteColumn>()
            .ExecuteAsync(boardId, owner.Id, lastId, seenNameVersion: 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Columns.Count);
        Assert.Equal(0, result.Value.Columns.Single(c => c.Id == firstId).Position);
        Assert.Equal(2, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'"));
    }

    // ---- AC-09: a column that still holds a card cannot be deleted --------------------------------

    [Fact]
    public async Task Deleting_a_column_that_still_holds_a_card_is_refused_and_nothing_changes()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A board with a busy column");
        var columnId = await FirstColumnIdAsync(boardId);
        await factory.ExecuteAsync(InsertCardSql(Guid.CreateVersion7(), boardId, columnId));
        await factory.ExecuteAsync(
            $"UPDATE [dbo].[Columns] SET [CardCount] = 1, [NextCardPosition] = 1 WHERE [Id] = '{columnId}';");
        await factory.ExecuteAsync(
            $"UPDATE [dbo].[Boards] SET [CardCount] = 1 WHERE [Id] = '{boardId}';");

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DeleteColumn>()
            .ExecuteAsync(boardId, owner.Id, columnId, seenNameVersion: 1, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.ColumnNotEmpty, result.Error);
        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [Id] = '{columnId}'"));
    }

    // ---- AC-10b: two forced-colliding deletes of the last two columns leave exactly one column -----

    [Fact]
    public async Task Two_deletes_of_the_last_two_columns_forced_to_collide_leave_exactly_one_column()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A board down to two columns");
        var (firstId, _, lastId) = await ThreeDefaultColumnIdsAsync(boardId);

        using (var trimScope = factory.Services.CreateScope())
        {
            var trimmed = await trimScope.ServiceProvider.GetRequiredService<DeleteColumn>()
                .ExecuteAsync(boardId, owner.Id, await SecondColumnIdAsync(boardId), 1, CancellationToken.None);
            Assert.True(trimmed.IsSuccess);
        }

        async Task<Domain.Result<ColumnLayout, BoardError>> DeleteAsync(Guid columnId)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<DeleteColumn>()
                .ExecuteAsync(boardId, owner.Id, columnId, 1, CancellationToken.None);
        }

        var results = await Task.WhenAll(DeleteAsync(firstId), DeleteAsync(lastId));

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        var refusal = results.Single(r => !r.IsSuccess);
        Assert.Same(BoardErrors.LastColumn, refusal.Error);
        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'"));
    }

    // ---- Two simultaneous renames of one column: one lands, the other names the winner -------------

    [Fact]
    public async Task Two_renames_of_one_column_at_once_let_only_one_land()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A board renamed twice at once");
        var columnId = await FirstColumnIdAsync(boardId);

        async Task<Domain.Result<BoardColumnView, BoardError>> RenameAsync(string name)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<RenameColumn>()
                .ExecuteAsync(boardId, owner.Id, columnId, name, 1, CancellationToken.None);
        }

        var results = await Task.WhenAll(RenameAsync("Racer A"), RenameAsync("Racer B"));

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        var winnerName = await factory.ScalarAsync<string>(
            $"SELECT [Name] FROM [dbo].[Columns] WHERE [Id] = '{columnId}'");
        Assert.True(winnerName is "Racer A" or "Racer B");

        var refusal = results.Single(r => !r.IsSuccess);
        Assert.Equal("boards.column_renamed", refusal.Error!.Code);
        Assert.Equal(winnerName, refusal.Error.CurrentColumn!.Name);
    }

    // ---- AC-18b: a column id from another board, and a deleted column's id, both answer NotAvailable

    [Fact]
    public async Task A_column_id_from_another_board_the_caller_belongs_to_answers_not_available()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "This board");
        var otherBoardId = await CreateBoardAsync(owner.Id, "Another board of the same owner");
        var foreignColumnId = await FirstColumnIdAsync(otherBoardId);

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RenameColumn>()
            .ExecuteAsync(boardId, owner.Id, foreignColumnId, "Hijacked", 1, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.NotAvailable, result.Error);
        Assert.Equal("To do", await factory.ScalarAsync<string>(
            $"SELECT [Name] FROM [dbo].[Columns] WHERE [Id] = '{foreignColumnId}'"));
    }

    [Fact]
    public async Task A_deleted_columns_id_answers_not_available()
    {
        var owner = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "A board that loses a column");
        var columnId = await FirstColumnIdAsync(boardId);

        using (var deleteScope = factory.Services.CreateScope())
        {
            var deleted = await deleteScope.ServiceProvider.GetRequiredService<DeleteColumn>()
                .ExecuteAsync(boardId, owner.Id, columnId, 1, CancellationToken.None);
            Assert.True(deleted.IsSuccess);
        }

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RenameColumn>()
            .ExecuteAsync(boardId, owner.Id, columnId, "Too late", 1, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.NotAvailable, result.Error);
    }

    // ---- Every one of the four begins with the member-scoped load: a non-member is refused ---------
    // ---- NotAvailable before anything about the board or its columns is revealed -------------------

    [Fact]
    public async Task A_non_member_is_refused_not_available_by_every_one_of_the_four()
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var boardId = await CreateBoardAsync(owner.Id, "Not the stranger's board");
        var columnId = await FirstColumnIdAsync(boardId);

        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        var added = await services.GetRequiredService<AddColumn>()
            .ExecuteAsync(boardId, stranger.Id, "Sneaky", CancellationToken.None);
        var renamed = await services.GetRequiredService<RenameColumn>()
            .ExecuteAsync(boardId, stranger.Id, columnId, "Sneaky", 1, CancellationToken.None);
        var moved = await services.GetRequiredService<MoveColumn>()
            .ExecuteAsync(boardId, stranger.Id, columnId, 1, 1, CancellationToken.None);
        var deleted = await services.GetRequiredService<DeleteColumn>()
            .ExecuteAsync(boardId, stranger.Id, columnId, 1, CancellationToken.None);

        Assert.Same(BoardErrors.NotAvailable, added.Error);
        Assert.Same(BoardErrors.NotAvailable, renamed.Error);
        Assert.Same(BoardErrors.NotAvailable, moved.Error);
        Assert.Same(BoardErrors.NotAvailable, deleted.Error);
        Assert.Equal(3, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'"));
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

    private Task InsertMembershipAsync(Guid boardId, Guid accountId, string role) =>
        factory.ExecuteAsync(
            $"""
            INSERT INTO [dbo].[BoardMemberships] ([Id], [BoardId], [AccountId], [Role])
            VALUES ('{Guid.CreateVersion7()}', '{boardId}', '{accountId}', N'{role}');
            """);

    private static string InsertCardSql(Guid id, Guid boardId, Guid columnId) =>
        $"""
        INSERT INTO [dbo].[Cards]
            ([Id], [BoardId], [ColumnId], [Position], [Title], [Description], [ContentVersion])
        VALUES ('{id}', '{boardId}', '{columnId}', 0, N'A card', N'', 1);
        """;

    private async Task<Guid> FirstColumnIdAsync(Guid boardId) =>
        await factory.ScalarAsync<Guid>(
            $"SELECT TOP 1 [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' ORDER BY [Position]");

    private async Task<Guid> SecondColumnIdAsync(Guid boardId) =>
        await factory.ScalarAsync<Guid>(
            $"""
            SELECT [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' ORDER BY [Position]
            OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY
            """);

    private async Task<(Guid First, Guid Second, Guid Last)> ThreeDefaultColumnIdsAsync(Guid boardId)
    {
        var ordered = await OrderedColumnIdsAsync(boardId);
        return (ordered[0], ordered[1], ordered[2]);
    }

    private async Task<IReadOnlyList<Guid>> OrderedColumnIdsAsync(Guid boardId)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(await factory.ScalarAsync<Guid>(
                $"""
                SELECT [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' ORDER BY [Position]
                OFFSET {i} ROWS FETCH NEXT 1 ROWS ONLY
                """));
        }

        return ids;
    }

    /// <summary>Fills a freshly created board (three columns) up to <paramref name="total"/> columns.</summary>
    private async Task FillColumnsUpToAsync(Guid boardId, int total)
    {
        for (var position = 3; position < total; position++)
        {
            await factory.ExecuteAsync(
                $"""
                INSERT INTO [dbo].[Columns]
                    ([Id], [BoardId], [Name], [Position], [CardCount], [NextCardPosition], [NameVersion])
                VALUES ('{Guid.CreateVersion7()}', '{boardId}', N'Filler {position}', {position}, 0, 0, 1);
                """);
        }
    }
}
