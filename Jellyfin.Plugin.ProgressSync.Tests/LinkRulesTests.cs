using System;
using System.Linq;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public class LinkRulesTests
{
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Partner = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static SyncLink Link(LinkMode mode, Guid? creator = null, Guid? partner = null)
        => new(Guid.NewGuid(), creator ?? Creator, partner ?? Partner, Guid.NewGuid(), mode, DateTime.UtcNow);

    [Theory]
    [InlineData(LinkMode.Follow, true, false)]
    [InlineData(LinkMode.Mirror, true, true)]
    public void Visibility_And_Removal(LinkMode mode, bool creatorSees, bool partnerSees)
    {
        var link = Link(mode);

        Assert.Equal(creatorSees, LinkRules.IsVisibleTo(link, Creator));
        Assert.Equal(partnerSees, LinkRules.IsVisibleTo(link, Partner));
        Assert.False(LinkRules.IsVisibleTo(link, Other));
        Assert.Equal(creatorSees, LinkRules.CanRemove(link, Creator));
        Assert.Equal(partnerSees, LinkRules.CanRemove(link, Partner));
        Assert.False(LinkRules.CanRemove(link, Other));
    }

    [Fact]
    public void Follow_FlowsFromPartnerToCreatorOnly()
    {
        var links = new[] { Link(LinkMode.Follow) };

        Assert.Equal([Creator], LinkRules.TargetsOf(links, Partner));
        Assert.Empty(LinkRules.TargetsOf(links, Creator));
    }

    [Fact]
    public void Mirror_FlowsBothWays()
    {
        var links = new[] { Link(LinkMode.Mirror) };

        Assert.Equal([Creator], LinkRules.TargetsOf(links, Partner));
        Assert.Equal([Partner], LinkRules.TargetsOf(links, Creator));
        Assert.Empty(LinkRules.TargetsOf(links, Other));
    }

    [Fact]
    public void FollowAndMirror_ProduceDuplicateTargets_ThatCallersDeduplicate()
    {
        var links = new[] { Link(LinkMode.Follow), Link(LinkMode.Mirror, Partner, Creator) };

        var targets = LinkRules.TargetsOf(links, Partner).ToList();

        Assert.Equal(2, targets.Count);
        Assert.Single(targets.Distinct());
    }

    [Fact]
    public void ResolveRoot_EpisodeAndSeason_UseSeries()
    {
        var seriesId = Guid.NewGuid();

        Assert.Equal(seriesId, LinkRules.ResolveRoot(new Episode { Id = Guid.NewGuid(), SeriesId = seriesId }));
        Assert.Equal(seriesId, LinkRules.ResolveRoot(new Season { Id = Guid.NewGuid(), SeriesId = seriesId }));
        Assert.Null(LinkRules.ResolveRoot(new Season { Id = Guid.NewGuid(), SeriesId = Guid.Empty }));
    }

    [Fact]
    public void ResolveRoot_SeriesAndMovies()
    {
        var series = new Series { Id = Guid.NewGuid() };
        var movie = new Movie { Id = Guid.NewGuid() };
        var primaryId = Guid.NewGuid();
        var alternate = new Movie { Id = Guid.NewGuid(), PrimaryVersionId = primaryId };

        Assert.Equal(series.Id, LinkRules.ResolveRoot(series));
        Assert.Equal(movie.Id, LinkRules.ResolveRoot(movie));
        Assert.Equal(primaryId, LinkRules.ResolveRoot(alternate));
        Assert.Null(LinkRules.ResolveRoot(new Folder { Id = Guid.NewGuid() }));
    }
}
