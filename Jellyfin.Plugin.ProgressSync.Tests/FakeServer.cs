using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Moq;

namespace Jellyfin.Plugin.ProgressSync.Tests;

/// <summary>
/// In-memory stand-in for the server's user data, library and user managers. SaveUserData raises
/// UserDataSaved synchronously, like the real UserDataManager.
/// </summary>
internal sealed class FakeServer
{
    public FakeServer()
    {
        UserData
            .Setup(m => m.GetUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<User>()))
            .Returns((IReadOnlyList<BaseItem> items, User user) => items.ToDictionary(i => i.Id, i => Get(user.Id, i.Id)));

        UserData
            .Setup(m => m.SaveUserData(It.IsAny<User>(), It.IsAny<BaseItem>(), It.IsAny<UserItemData>(), It.IsAny<UserDataSaveReason>(), It.IsAny<CancellationToken>()))
            .Callback((User user, BaseItem item, UserItemData data, UserDataSaveReason reason, CancellationToken _) =>
            {
                if (ThrowOnSaveFor.Contains(user.Id))
                {
                    throw new InvalidOperationException("simulated failure");
                }

                Data[(user.Id, item.Id)] = data;
                Saves.Add((user.Id, item.Id, reason));
                UserData.Raise(m => m.UserDataSaved += null, new UserDataSaveEventArgs
                {
                    UserId = user.Id,
                    Item = item,
                    UserData = data,
                    SaveReason = reason,
                    Keys = []
                });
            });
    }

    public Mock<IUserDataManager> UserData { get; } = new();

    public Mock<ILibraryManager> Library { get; } = new();

    public Mock<IUserManager> Users { get; } = new();

    public Dictionary<(Guid User, Guid Item), UserItemData> Data { get; } = [];

    public List<(Guid User, Guid Item, UserDataSaveReason Reason)> Saves { get; } = [];

    /// <summary>Gets (user, item) pairs the user cannot access.</summary>
    public HashSet<(Guid User, Guid Item)> Hidden { get; } = [];

    public HashSet<Guid> ThrowOnSaveFor { get; } = [];

    public UserItemData Get(Guid userId, Guid itemId)
        => Data.TryGetValue((userId, itemId), out var data) ? data : new UserItemData { Key = "key" };

    public List<User> AllUsers { get; } = [];

    public User AddUser(string name)
    {
        var user = new User(name, "provider", "reset") { Id = Guid.NewGuid() };
        Users.Setup(m => m.GetUserById(user.Id)).Returns(user);
        AllUsers.Add(user);
        Users.Setup(m => m.GetUsers()).Returns(AllUsers);
        return user;
    }

    /// <summary>Registers an item; GetItemById returns <paramref name="cached"/> (or the item) unless hidden.</summary>
    public T AddItem<T>(T item, BaseItem? cached = null)
        where T : BaseItem
    {
        var instance = cached ?? item;
        Library
            .Setup(m => m.GetItemById<BaseItem>(item.Id, It.IsAny<User?>()))
            .Returns((Guid id, User? user) => user is not null && Hidden.Contains((user.Id, id)) ? null : instance);
        return item;
    }

    public static UserItemData Progress(long ticks, bool played = false, int playCount = 0)
        => new() { Key = "key", PlaybackPositionTicks = ticks, Played = played, PlayCount = playCount };
}
