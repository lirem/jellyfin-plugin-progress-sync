using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Tracks which users have already been written during one propagation of a progress change.
/// </summary>
/// <remarks>
/// The context is thread-bound. That is correct because the server raises
/// <c>IUserDataManager.UserDataSaved</c> synchronously on the thread that saved, so writes made by
/// the plugin re-enter its event handler on the same thread. Unlike an <c>AsyncLocal</c>, it does
/// not leak into timers or tasks started by other event subscribers. Never <c>await</c> between
/// <see cref="Begin"/> and <see cref="Dispose"/>.
/// </remarks>
internal sealed class PropagationContext : IDisposable
{
    [ThreadStatic]
    private static PropagationContext? _current;

    private PropagationContext(IEnumerable<Guid> reached)
    {
        Reached = new HashSet<Guid>(reached);
    }

    /// <summary>
    /// Gets the context active on the current thread, if any.
    /// </summary>
    public static PropagationContext? Current => _current;

    /// <summary>
    /// Gets the users that originated or already received this change.
    /// </summary>
    public HashSet<Guid> Reached { get; }

    /// <summary>
    /// Starts a context on the current thread.
    /// </summary>
    /// <param name="reached">Users that must not receive the change.</param>
    /// <returns>The context; dispose it to end the propagation.</returns>
    /// <exception cref="InvalidOperationException">A context is already active on this thread.</exception>
    public static PropagationContext Begin(params Guid[] reached)
    {
        if (_current is not null)
        {
            throw new InvalidOperationException("A propagation context is already active on this thread.");
        }

        _current = new PropagationContext(reached);
        return _current;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }
    }
}
