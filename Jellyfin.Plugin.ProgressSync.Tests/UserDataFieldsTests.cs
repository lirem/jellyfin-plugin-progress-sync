using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.ProgressSync.Services;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public class UserDataFieldsTests
{
    private static UserItemData Source() => new()
    {
        Key = "source-key",
        PlaybackPositionTicks = 123,
        Played = true,
        PlayCount = 3,
        LastPlayedDate = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        IsFavorite = true,
        Rating = 2,
        AudioStreamIndex = 1,
        SubtitleStreamIndex = 2
    };

    private static UserItemData Target() => new()
    {
        Key = "target-key",
        PlaybackPositionTicks = 0,
        Played = false,
        PlayCount = 0,
        LastPlayedDate = null,
        IsFavorite = false,
        Rating = 7.5,
        AudioStreamIndex = 5,
        SubtitleStreamIndex = 6
    };

    [Fact]
    public void CloneWithProgressFrom_CopiesOnlyProgressFields()
    {
        var source = Source();
        var target = Target();

        var result = UserDataFields.CloneWithProgressFrom(target, source);

        Assert.Equal(123, result.PlaybackPositionTicks);
        Assert.True(result.Played);
        Assert.Equal(3, result.PlayCount);
        Assert.Equal(source.LastPlayedDate, result.LastPlayedDate);

        Assert.Equal("target-key", result.Key);
        Assert.False(result.IsFavorite);
        Assert.Equal(7.5, result.Rating);
        Assert.Equal(5, result.AudioStreamIndex);
        Assert.Equal(6, result.SubtitleStreamIndex);
    }

    [Fact]
    public void CloneWithProgressFrom_DoesNotMutateInputs()
    {
        var source = Source();
        var target = Target();

        var result = UserDataFields.CloneWithProgressFrom(target, source);

        Assert.NotSame(target, result);
        Assert.NotSame(source, result);
        Assert.Equal(0, target.PlaybackPositionTicks);
        Assert.False(target.Played);
        Assert.Equal(7.5, target.Rating);
        Assert.Equal(2, source.Rating);
    }

    [Fact]
    public void CloneWithProgressFrom_CanResetToUnplayed()
    {
        var result = UserDataFields.CloneWithProgressFrom(Source(), Target());

        Assert.Equal(0, result.PlaybackPositionTicks);
        Assert.False(result.Played);
        Assert.Equal(0, result.PlayCount);
        Assert.Null(result.LastPlayedDate);
    }

    [Fact]
    public void ProgressEquals_IgnoresNonProgressFields()
    {
        var a = Source();
        var b = UserDataFields.CloneWithProgressFrom(Target(), a);

        Assert.True(UserDataFields.ProgressEquals(a, b));

        b.PlaybackPositionTicks++;
        Assert.False(UserDataFields.ProgressEquals(a, b));
    }

    [Fact]
    public void UserItemData_HasNoFieldsUnknownToTheClone()
    {
        // CloneWithProgressFrom copies an explicit list of fields. If a server update adds a field to
        // UserItemData, it would be reset on every copy; this test makes such an update fail in CI.
        var known = new HashSet<string>
        {
            "Key", "Rating", "PlaybackPositionTicks", "PlayCount", "IsFavorite", "LastPlayedDate",
            "Played", "AudioStreamIndex", "SubtitleStreamIndex", "Likes"
        };
        var settable = typeof(UserItemData)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name);

        Assert.Empty(settable.Except(known));
    }
}
