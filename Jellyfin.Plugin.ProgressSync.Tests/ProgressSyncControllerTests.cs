using System;
using System.IO;
using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.ProgressSync.Api;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

/// <summary>
/// The REST API is the security boundary: only the caller's own data is ever written.
/// </summary>
public sealed class ProgressSyncControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ps-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeServer _server = new();
    private readonly LinkStore _store;
    private readonly ProgressCopier _copier;
    private readonly User _alice;
    private readonly User _bob;
    private readonly Series _series;
    private readonly Episode _episode;
    private readonly Movie _movie;

    public ProgressSyncControllerTests()
    {
        _store = new LinkStore(Path.Combine(_dir, "links.json"), NullLogger<LinkStore>.Instance);
        _copier = new ProgressCopier(_server.Library.Object, _server.UserData.Object, NullLogger<ProgressCopier>.Instance);
        _alice = _server.AddUser("alice");
        _bob = _server.AddUser("bob");
        _series = _server.AddItem(new Series { Id = Guid.NewGuid(), Name = "Show" });
        _episode = _server.AddItem(new Episode { Id = Guid.NewGuid(), SeriesId = _series.Id });
        _movie = _server.AddItem(new Movie { Id = Guid.NewGuid(), Name = "Movie" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private ProgressSyncController As(User? caller, string? rawClaim = null)
    {
        var controller = new ProgressSyncController(
            _server.Library.Object,
            _server.Users.Object,
            _copier,
            _store,
            new WebInjectionStatus(),
            NullLogger<ProgressSyncController>.Instance);
        var claimValue = rawClaim ?? caller?.Id.ToString("N");
        Claim[] claims = claimValue is null ? [] : [new Claim("Jellyfin-UserId", claimValue)];
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return controller;
    }

    private static int? Status<T>(ActionResult<T> result) => (result.Result as IStatusCodeActionResult)?.StatusCode;

    private static int? Status(ActionResult result) => (result as IStatusCodeActionResult)?.StatusCode;

    [Fact]
    public void CallersWithoutAUser_AreForbidden()
    {
        // No claim, and an API key (empty user id).
        foreach (var controller in new[] { As(null), As(null, Guid.Empty.ToString("N")) })
        {
            Assert.Equal(403, Status(controller.CopyFrom(_movie.Id, _alice.Id)));
            Assert.Equal(403, Status(controller.GetLinks(_movie.Id)));
            Assert.Equal(403, Status(controller.CreateLink(_movie.Id, new CreateLinkRequest(_alice.Id, LinkMode.Follow))));
            Assert.Equal(403, Status(controller.DeleteLink(Guid.NewGuid())));
        }
    }

    [Fact]
    public void CopyFrom_RejectsSelfUnknownAndInaccessible()
    {
        var bob = As(_bob);

        Assert.Equal(400, Status(bob.CopyFrom(_movie.Id, _bob.Id)));
        Assert.Equal(404, Status(bob.CopyFrom(_movie.Id, Guid.NewGuid())));
        Assert.Equal(404, Status(bob.CopyFrom(_movie.Id, Guid.Empty)));
        Assert.Equal(404, Status(bob.CopyFrom(Guid.Empty, _alice.Id)));

        _server.Hidden.Add((_bob.Id, _movie.Id));
        Assert.Equal(404, Status(bob.CopyFrom(_movie.Id, _alice.Id)));
        Assert.Empty(_server.Saves);
    }

    [Fact]
    public void CopyFrom_UnsupportedType_IsBadRequest()
    {
        var folder = _server.AddItem(new Folder { Id = Guid.NewGuid() });

        Assert.Equal(400, Status(As(_bob).CopyFrom(folder.Id, _alice.Id)));
    }

    [Fact]
    public void CreateLink_ValidatesModePartnerAndAccess()
    {
        var bob = As(_bob);

        Assert.Equal(400, Status(bob.CreateLink(_episode.Id, new CreateLinkRequest(_alice.Id, null))));
        Assert.Equal(400, Status(bob.CreateLink(_episode.Id, new CreateLinkRequest(_alice.Id, (LinkMode)42))));
        Assert.Equal(400, Status(bob.CreateLink(_episode.Id, new CreateLinkRequest(Guid.Empty, LinkMode.Follow))));
        Assert.Equal(400, Status(bob.CreateLink(_episode.Id, new CreateLinkRequest(_bob.Id, LinkMode.Follow))));
        Assert.Equal(404, Status(bob.CreateLink(_episode.Id, new CreateLinkRequest(Guid.NewGuid(), LinkMode.Follow))));

        // The series must be visible to the partner as well.
        _server.Hidden.Add((_alice.Id, _series.Id));
        Assert.Equal(404, Status(bob.CreateLink(_episode.Id, new CreateLinkRequest(_alice.Id, LinkMode.Mirror))));
        Assert.Empty(_store.Snapshot.All);
    }

    [Fact]
    public void CreateLink_FromEpisode_BindsToSeries_WithCallerAsCreator()
    {
        var result = As(_bob).CreateLink(_episode.Id, new CreateLinkRequest(_alice.Id, LinkMode.Follow));

        Assert.NotNull(result.Value);
        var link = Assert.Single(_store.Snapshot.All);
        Assert.Equal(_bob.Id, link.CreatorUserId);
        Assert.Equal(_alice.Id, link.PartnerUserId);
        Assert.Equal(_series.Id, link.RootItemId);
        Assert.Equal(LinkMode.Follow, link.Mode);
    }

    [Fact]
    public void FollowLink_IsInvisibleAndNotRemovableForPartner()
    {
        var link = _store.Upsert(_bob.Id, _alice.Id, _series.Id, LinkMode.Follow, _ => false);

        Assert.Empty(As(_alice).GetLinks(_episode.Id).Value!);
        Assert.Equal(404, Status(As(_alice).DeleteLink(link.Id)));
        Assert.NotNull(_store.Snapshot.Find(link.Id));

        var bobsView = Assert.Single(As(_bob).GetLinks(_episode.Id).Value!);
        Assert.Equal(_alice.Id, bobsView.PartnerUserId);
        Assert.True(bobsView.CanRemove);
        Assert.Equal(204, Status(As(_bob).DeleteLink(link.Id)));
        Assert.Empty(_store.Snapshot.All);
    }

    [Fact]
    public void MirrorLink_IsVisibleAndRemovableForPartner()
    {
        var link = _store.Upsert(_bob.Id, _alice.Id, _series.Id, LinkMode.Mirror, _ => false);

        var alicesView = Assert.Single(As(_alice).GetLinks(_series.Id).Value!);
        Assert.Equal(_bob.Id, alicesView.PartnerUserId);
        Assert.False(alicesView.IsCreator);
        Assert.Equal(204, Status(As(_alice).DeleteLink(link.Id)));
        Assert.Empty(_store.Snapshot.All);
    }
}
