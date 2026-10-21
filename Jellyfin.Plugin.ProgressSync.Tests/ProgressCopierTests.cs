using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ProgressSync.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public class ProgressCopierTests
{
    private readonly FakeServer _server = new();
    private readonly ProgressCopier _copier;

    public ProgressCopierTests()
    {
        _copier = new ProgressCopier(_server.Library.Object, _server.UserData.Object, NullLogger<ProgressCopier>.Instance);
    }

    [Fact]
    public void CopyItems_WritesOnlyDifferingItems_KeepingTargetsOtherFields()
    {
        var source = _server.AddUser("alice");
        var target = _server.AddUser("bob");
        var differs = new Movie { Id = Guid.NewGuid() };
        var same = new Movie { Id = Guid.NewGuid() };
        _server.Data[(source.Id, differs.Id)] = FakeServer.Progress(500, played: true, playCount: 2);
        var targetBefore = FakeServer.Progress(0);
        targetBefore.IsFavorite = true;
        targetBefore.Rating = 7.5;
        _server.Data[(target.Id, differs.Id)] = targetBefore;
        _server.Data[(source.Id, same.Id)] = FakeServer.Progress(42);
        _server.Data[(target.Id, same.Id)] = FakeServer.Progress(42);

        var result = _copier.CopyItems(source, target, new List<BaseItem> { differs, same }, UserDataSaveReason.UpdateUserData);

        Assert.Equal(1, result.Updated);
        Assert.Equal(2, result.Total);
        var save = Assert.Single(_server.Saves);
        Assert.Equal((target.Id, differs.Id, UserDataSaveReason.UpdateUserData), save);

        var after = _server.Get(target.Id, differs.Id);
        Assert.Equal(500, after.PlaybackPositionTicks);
        Assert.True(after.Played);
        Assert.Equal(2, after.PlayCount);
        Assert.True(after.IsFavorite);
        Assert.Equal(7.5, after.Rating);

        // The instance the "cache" returned was replaced, not mutated.
        Assert.NotSame(targetBefore, after);
        Assert.Equal(0, targetBefore.PlaybackPositionTicks);
    }

    [Fact]
    public void CopyItems_ContextDuringSave_ExcludesSourceAndTarget_AndIsClearedAfterwards()
    {
        var source = _server.AddUser("alice");
        var target = _server.AddUser("bob");
        var movie = new Movie { Id = Guid.NewGuid() };
        _server.Data[(source.Id, movie.Id)] = FakeServer.Progress(10);
        HashSet<Guid>? reachedDuringSave = null;
        _server.UserData.Object.UserDataSaved += (_, _) => reachedDuringSave = [.. PropagationContext.Current!.Reached];

        _copier.CopyItems(source, target, new List<BaseItem> { movie }, UserDataSaveReason.UpdateUserData);

        Assert.NotNull(reachedDuringSave);
        Assert.Equal(new HashSet<Guid> { source.Id, target.Id }, reachedDuringSave);
        Assert.Null(PropagationContext.Current);
    }

    [Fact]
    public void FilterVisible_KeepsOnlyItemsBothUsersCanAccess_AndReturnsCachedInstances()
    {
        var source = _server.AddUser("alice");
        var target = _server.AddUser("bob");
        var visible = new Movie { Id = Guid.NewGuid() };
        var cachedVisible = new Movie { Id = visible.Id };
        var hiddenFromSource = new Movie { Id = Guid.NewGuid() };
        var hiddenFromTarget = new Movie { Id = Guid.NewGuid() };
        _server.AddItem(visible, cachedVisible);
        _server.AddItem(hiddenFromSource);
        _server.AddItem(hiddenFromTarget);
        _server.Hidden.Add((source.Id, hiddenFromSource.Id));
        _server.Hidden.Add((target.Id, hiddenFromTarget.Id));

        var scope = _copier.FilterVisible(new List<BaseItem> { visible, hiddenFromSource, hiddenFromTarget }, target, source);

        var item = Assert.Single(scope.Items);
        Assert.Same(cachedVisible, item);
        Assert.Equal(2, scope.Skipped);
    }
}
