using Xunit;
using CanfarDesktop.Services.Notebook;

namespace CanfarDesktop.Tests.Services.Notebook;

public class DirtyTrackerTests
{
    [Fact]
    public void InitialState_IsClean()
    {
        var tracker = new DirtyTracker();
        Assert.False(tracker.IsDirty);
    }

    [Fact]
    public void MarkDirty_SetsFlag()
    {
        var tracker = new DirtyTracker();
        tracker.MarkDirty();
        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void MarkDirty_FiresEvent()
    {
        var tracker = new DirtyTracker();
        bool? eventValue = null;
        tracker.DirtyChanged += v => eventValue = v;

        tracker.MarkDirty();

        Assert.True(eventValue);
    }

    [Fact]
    public void MarkDirty_Twice_FiresOnce()
    {
        var tracker = new DirtyTracker();
        var fireCount = 0;
        tracker.DirtyChanged += _ => fireCount++;

        tracker.MarkDirty();
        tracker.MarkDirty();

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void MarkClean_ClearsFlag()
    {
        var tracker = new DirtyTracker();
        tracker.MarkDirty();
        tracker.MarkClean();

        Assert.False(tracker.IsDirty);
    }

    [Fact]
    public void MarkClean_FiresEvent()
    {
        var tracker = new DirtyTracker();
        tracker.MarkDirty();

        bool? eventValue = null;
        tracker.DirtyChanged += v => eventValue = v;
        tracker.MarkClean();

        Assert.False(eventValue);
    }

    [Fact]
    public void MarkClean_WhenAlreadyClean_NoEvent()
    {
        var tracker = new DirtyTracker();
        var fireCount = 0;
        tracker.DirtyChanged += _ => fireCount++;

        tracker.MarkClean();

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void Reset_ClearsAndFiresEvent()
    {
        var tracker = new DirtyTracker();
        tracker.MarkDirty();

        bool? eventValue = null;
        tracker.DirtyChanged += v => eventValue = v;
        tracker.Reset();

        Assert.False(tracker.IsDirty);
        Assert.False(eventValue);
    }

    [Fact]
    public void DirtyCleanCycle_TracksCorrectly()
    {
        var tracker = new DirtyTracker();
        var events = new List<bool>();
        tracker.DirtyChanged += v => events.Add(v);

        tracker.MarkDirty();   // true
        tracker.MarkClean();   // false
        tracker.MarkDirty();   // true
        tracker.MarkClean();   // false

        Assert.Equal(4, events.Count);
        Assert.Equal([true, false, true, false], events);
    }
}
