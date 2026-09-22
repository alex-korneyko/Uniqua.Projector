using Uniqua.Projector.Domain;

namespace Uniqua.Projector.Domain.Tests;

/// <summary>
/// The id strategy is a foundation rule rather than a feature detail, so it is pinned here: ids
/// must be time-ordered, otherwise the clustered-index argument behind choosing GUID v7 collapses.
/// </summary>
public sealed class IdsTests
{
    [Fact]
    public void New_ids_sort_in_the_order_they_were_created()
    {
        var earlier = Ids.New(DateTimeOffset.UtcNow.AddMinutes(-1));
        var later = Ids.New(DateTimeOffset.UtcNow);

        Assert.True(earlier.CompareTo(later) < 0);
    }

    [Fact]
    public void New_ids_are_version_7()
    {
        Assert.Equal(7, Ids.New().Version);
    }
}
