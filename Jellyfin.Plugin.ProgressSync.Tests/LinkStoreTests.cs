using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public sealed class LinkStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ps-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "sub", "links.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private LinkStore NewStore() => new(FilePath, NullLogger<LinkStore>.Instance);

    private static bool NotStale(SyncLink link) => false;

    [Fact]
    public void Upsert_CreatesThenUpdatesMode_KeepingIdentity()
    {
        var store = NewStore();
        var (a, b, root) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var created = store.Upsert(a, b, root, LinkMode.Follow, NotStale);
        var updated = store.Upsert(a, b, root, LinkMode.Mirror, NotStale);

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(created.CreatedUtc, updated.CreatedUtc);
        Assert.Equal(LinkMode.Mirror, store.Snapshot.Find(created.Id)!.Mode);
        Assert.Single(store.Snapshot.All);
        Assert.Single(store.Snapshot.ForRoot(root));
        Assert.Empty(store.Snapshot.ForRoot(Guid.NewGuid()));
    }

    [Fact]
    public void Links_PersistAcrossInstances_AndBackupIsWritten()
    {
        var first = NewStore();
        var link = first.Upsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LinkMode.Mirror, NotStale);
        first.Upsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LinkMode.Follow, NotStale);

        var second = NewStore();

        Assert.Equal(2, second.Snapshot.All.Count);
        Assert.Equal(link, second.Snapshot.Find(link.Id));
        Assert.True(File.Exists(FilePath + ".bak"));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Remove_DeletesLink_AndReportsExistence()
    {
        var store = NewStore();
        var link = store.Upsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LinkMode.Follow, NotStale);

        Assert.True(store.Remove(link.Id, NotStale));
        Assert.False(store.Remove(link.Id, NotStale));
        Assert.Empty(NewStore().Snapshot.All);
    }

    [Fact]
    public void Mutations_PruneStaleLinks()
    {
        var store = NewStore();
        var deletedUser = Guid.NewGuid();
        store.Upsert(deletedUser, Guid.NewGuid(), Guid.NewGuid(), LinkMode.Follow, NotStale);

        store.Upsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LinkMode.Mirror, l => l.CreatorUserId == deletedUser);

        Assert.Single(store.Snapshot.All);
        Assert.DoesNotContain(store.Snapshot.All, l => l.CreatorUserId == deletedUser);
    }

    [Fact]
    public void CorruptFile_FallsBackToBackup()
    {
        var store = NewStore();
        var link = store.Upsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LinkMode.Follow, NotStale);
        store.Upsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LinkMode.Follow, NotStale);
        File.WriteAllText(FilePath, "{ not json");

        var reloaded = NewStore();

        Assert.Equal(link, reloaded.Snapshot.Find(link.Id));
    }

    [Fact]
    public void ConcurrentUpserts_AreAllPersisted()
    {
        var store = NewStore();

        Parallel.For(0, 40, _ => store.Upsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LinkMode.Follow, NotStale));

        Assert.Equal(40, store.Snapshot.All.Count);
        Assert.Equal(40, NewStore().Snapshot.All.Select(l => l.Id).Distinct().Count());
    }

    [Fact]
    public void NullEntries_AreSkipped()
    {
        var link = new SyncLink(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LinkMode.Mirror, DateTime.UtcNow);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{\"Version\":1,\"Links\":[null,{\"Id\":\"" + link.Id + "\",\"CreatorUserId\":\"" + link.CreatorUserId
            + "\",\"PartnerUserId\":\"" + link.PartnerUserId + "\",\"RootItemId\":\"" + link.RootItemId + "\",\"Mode\":\"Mirror\",\"CreatedUtc\":\"2026-01-01T00:00:00Z\"}]}");

        var loaded = Assert.Single(NewStore().Snapshot.All);
        Assert.Equal(link.Id, loaded.Id);
    }

    [Fact]
    public void UnreadableFile_IsNeverOverwritten()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var store = NewStore();
        store.Upsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LinkMode.Follow, NotStale);
        var original = File.ReadAllText(FilePath);
        File.SetUnixFileMode(FilePath, UnixFileMode.None);
        try
        {
            var locked = NewStore();

            Assert.Empty(locked.Snapshot.All);
            Assert.Throws<InvalidOperationException>(() => locked.Upsert(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LinkMode.Follow, NotStale));
        }
        finally
        {
            File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Assert.Equal(original, File.ReadAllText(FilePath));
    }
}
