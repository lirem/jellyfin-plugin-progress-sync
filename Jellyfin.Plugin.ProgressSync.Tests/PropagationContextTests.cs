using System;
using System.Threading;
using Jellyfin.Plugin.ProgressSync.Services;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public class PropagationContextTests
{
    [Fact]
    public void Begin_SetsCurrent_DisposeClearsIt()
    {
        var user = Guid.NewGuid();
        Assert.Null(PropagationContext.Current);

        using (var context = PropagationContext.Begin(user))
        {
            Assert.Same(context, PropagationContext.Current);
            Assert.Contains(user, context.Reached);
        }

        Assert.Null(PropagationContext.Current);
    }

    [Fact]
    public void Begin_WhileActive_Throws()
    {
        using var context = PropagationContext.Begin(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => PropagationContext.Begin(Guid.NewGuid()));
    }

    [Fact]
    public void Context_IsPerThread()
    {
        using var context = PropagationContext.Begin(Guid.NewGuid());
        PropagationContext? seenOnOtherThread = context;

        var thread = new Thread(() => seenOnOtherThread = PropagationContext.Current);
        thread.Start();
        thread.Join();

        Assert.Null(seenOnOtherThread);
    }
}
