using CanfarDesktop.Services.Workflows;
using Xunit;

namespace CanfarDesktop.Tests.Services.Workflows;

public class WorkflowStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vb-wf-" + Guid.NewGuid().ToString("N"));

    private WorkflowStore NewStore(params (string, string)[] builtins)
        => new(_dir, () => builtins.ToList());

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void SaveNew_ListsGetsAndDeduplicatesSlugs()
    {
        var store = NewStore();
        var events = 0;
        var lastChangedId = (string?)null;
        store.Changed += id => { events++; lastChangedId = id; };

        var id1 = store.SaveNew("M31 Run!", WorkflowFormat.Skeleton("M31 Run!"));
        var id2 = store.SaveNew("M31 Run!", WorkflowFormat.Skeleton("M31 Run!"));
        Assert.Equal("local:m31-run", id1);
        Assert.Equal("local:m31-run-2", id2);
        Assert.Equal(2, events);
        Assert.Equal(id2, lastChangedId); // Changed carries the affected id (follow-agent-activity)

        var list = store.ListLocal();
        Assert.Equal(2, list.Count);
        Assert.All(list, w => Assert.Equal(WorkflowSource.Local, w.Source));

        Assert.Equal("M31 Run!", store.Get(id1)!.Doc.Title);
        Assert.Null(store.Get("local:nope"));
    }

    [Fact]
    public void SetStepDone_PersistsIntoTheFile()
    {
        var store = NewStore();
        var id = store.SaveNew("t", WorkflowFormat.Skeleton("t"));

        store.SetStepDone(id, 0, true);
        var doc = store.Get(id)!.Doc;
        Assert.True(doc.Steps[0].Done);
        Assert.False(doc.Steps[1].Done);
        Assert.Equal(1, doc.DoneCount);

        store.SetStepDone(id, 0, false);
        Assert.Equal(0, store.Get(id)!.Doc.DoneCount);
    }

    [Fact]
    public void BuiltIn_IsReadOnly_AndUseWorkflowCopiesIt()
    {
        var store = NewStore(("demo", "# Demo\n- [ ] **A** — x\n"));

        var builtin = Assert.Single(store.ListBuiltIn());
        Assert.Equal("builtin:demo", builtin.Id);
        Assert.NotNull(store.Get("builtin:demo"));

        var ex = Assert.Throws<InvalidOperationException>(() => store.SetStepDone("builtin:demo", 0, true));
        Assert.Contains("use_workflow", ex.Message); // actionable: tells the agent the remedy

        var localId = store.UseWorkflow("builtin:demo");
        Assert.StartsWith("local:", localId);
        store.SetStepDone(localId, 0, true); // the copy tracks progress
        Assert.True(store.Get(localId)!.Doc.Steps[0].Done);
        Assert.False(store.Get("builtin:demo")!.Doc.Steps[0].Done); // source untouched
    }

    [Fact]
    public void UpdateText_ReplacesContent_AndDeleteRemoves()
    {
        var store = NewStore();
        var id = store.SaveNew("t", "# Old\n- [ ] **A** — x\n");
        store.UpdateText(id, "# New title\n- [ ] **B** — y\n");
        Assert.Equal("New title", store.Get(id)!.Doc.Title);

        store.Delete(id);
        Assert.Null(store.Get(id));
        Assert.Throws<InvalidOperationException>(() => store.UpdateText(id, "x"));
    }

    [Fact]
    public void Slugify_NormalizesNames()
    {
        Assert.Equal("m31-archival-recon", WorkflowStore.Slugify("  M31: Archival Recon!  "));
        Assert.Equal("workflow", WorkflowStore.Slugify("???"));
    }
}
