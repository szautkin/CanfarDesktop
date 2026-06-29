using Xunit;
using CanfarDesktop.Services.AiGuide;
using CanfarDesktop.Services.Database;

namespace CanfarDesktop.Tests.Services;

/// <summary>
/// Mirrors the macOS <c>AIGuideServiceTests.swift</c>: description-override set/clear/persist, guide
/// CRUD, slug sanitization, validation, snapshot + body fallback, and the UI row merge — all on a
/// private in-memory SQLite database.
/// </summary>
public class AiGuideServiceTests : IDisposable
{
    private readonly AppDatabase _db = new(filePath: null); // private in-memory database
    private readonly AiGuideStore _store;
    private readonly AiGuideService _service;

    public AiGuideServiceTests()
    {
        _store = new AiGuideStore(_db, deviceId: "test-device");
        _service = new AiGuideService(_store);
    }

    public void Dispose() => _db.Dispose();

    // MARK: - Description overrides

    [Fact]
    public void SetOverride_ThenSnapshot_HasOverride()
    {
        _service.SetOverride("search_observations", "  Find images by sky position.  ");

        Assert.True(_service.IsOverridden("search_observations"));
        Assert.Equal("Find images by sky position.",
            _service.EffectiveDescription("search_observations", "default"));
        Assert.Equal("Find images by sky position.",
            _service.Snapshot().DescriptionForTool("search_observations", "default"));
    }

    [Fact]
    public void SetOverride_EmptyText_ClearsOverride()
    {
        _service.SetOverride("get_auth_state", "something");
        _service.SetOverride("get_auth_state", "   ");

        Assert.False(_service.IsOverridden("get_auth_state"));
        Assert.Equal("builtin", _service.EffectiveDescription("get_auth_state", "builtin"));
    }

    [Fact]
    public void SetOverride_TooLong_Throws()
    {
        var ex = Assert.Throws<AiGuideValidationException>(
            () => _service.SetOverride("x", new string('a', AiGuideService.MaxDescriptionChars + 1)));
        Assert.Equal(AiGuideErrorKind.TooLong, ex.Kind);
    }

    [Fact]
    public void ClearOverride_RemovesOverride()
    {
        _service.SetOverride("list_sessions", "mine");
        _service.ClearOverride("list_sessions");

        Assert.False(_service.IsOverridden("list_sessions"));
    }

    [Fact]
    public void Overrides_PersistAcrossReload()
    {
        _service.SetOverride("navigate_to", "Jump around the app.");

        // A fresh service over the same connection hydrates from the DB.
        var reopened = new AiGuideService(_store);
        Assert.True(reopened.IsOverridden("navigate_to"));
        Assert.Equal("Jump around the app.",
            reopened.EffectiveDescription("navigate_to", "default"));
    }

    // MARK: - Guide tools

    [Fact]
    public void AddGuide_AppearsInGuides_AndSnapshot()
    {
        var entry = _service.AddGuide("Survey Tips", "How to run a survey", "Step 1. Step 2.");

        Assert.Equal("survey_tips", entry.Name);
        var snap = _service.Snapshot();
        Assert.Contains(snap.Guides, g => g.Name == "survey_tips");
        Assert.Equal("Step 1. Step 2.", snap.GuideBody("survey_tips"));
    }

    [Fact]
    public void AddGuide_SlugifiesName()
    {
        var entry = _service.AddGuide("My  Cool-Tool.v2", "desc", null);
        Assert.Equal("my_cool_tool_v2", entry.Name);
    }

    [Fact]
    public void AddGuide_DuplicateName_Throws()
    {
        _service.AddGuide("dup", "first", null);
        var ex = Assert.Throws<AiGuideValidationException>(() => _service.AddGuide("DUP", "second", null));
        Assert.Equal(AiGuideErrorKind.NameTaken, ex.Kind);
    }

    [Fact]
    public void AddGuide_NameCollidesWithBuiltin_Throws()
    {
        _service.KnownToolNames = new HashSet<string> { "search_observations" };
        var ex = Assert.Throws<AiGuideValidationException>(
            () => _service.AddGuide("Search Observations", "desc", null));
        Assert.Equal(AiGuideErrorKind.NameCollidesWithTool, ex.Kind);
    }

    [Fact]
    public void AddGuide_CollidesWithBuiltin_ByDefault_WithoutHostStart()
    {
        // A fresh service (host never started) still rejects a name that shadows a live built-in,
        // because KnownToolNames defaults to the catalog's known names.
        var ex = Assert.Throws<AiGuideValidationException>(
            () => _service.AddGuide("navigate to", "desc", null)); // slugs to "navigate_to" (a real tool)
        Assert.Equal(AiGuideErrorKind.NameCollidesWithTool, ex.Kind);
    }

    [Fact]
    public void AddGuide_EmptyNameAfterSlug_Throws()
    {
        var ex = Assert.Throws<AiGuideValidationException>(() => _service.AddGuide("界面", "desc", null));
        Assert.Equal(AiGuideErrorKind.NameEmpty, ex.Kind);
    }

    [Fact]
    public void AddGuide_EmptyDescription_Throws()
    {
        var ex = Assert.Throws<AiGuideValidationException>(() => _service.AddGuide("ok", "   ", "body"));
        Assert.Equal(AiGuideErrorKind.DescriptionEmpty, ex.Kind);
    }

    [Fact]
    public void AddGuide_BodyTooLong_Throws()
    {
        var ex = Assert.Throws<AiGuideValidationException>(
            () => _service.AddGuide("ok", "desc", new string('b', AiGuideService.MaxBodyChars + 1)));
        Assert.Equal(AiGuideErrorKind.TooLong, ex.Kind);
    }

    [Fact]
    public void UpdateGuide_ChangesFields()
    {
        var entry = _service.AddGuide("orig", "old desc", "old body");
        _service.UpdateGuide(entry.Id, "renamed", "new desc", "new body");

        var snap = _service.Snapshot();
        Assert.DoesNotContain(snap.Guides, g => g.Name == "orig");
        var updated = Assert.Single(snap.Guides, g => g.Name == "renamed");
        Assert.Equal("new desc", updated.Description);
        Assert.Equal("new body", updated.Body);
    }

    [Fact]
    public void DeleteGuide_SoftDeletes_NameReusable()
    {
        var entry = _service.AddGuide("temp", "desc", null);
        _service.DeleteGuide(entry.Id);

        Assert.Empty(_service.Snapshot().Guides);
        // The slug is free again after a soft-delete.
        var revived = _service.AddGuide("Temp", "desc2", null);
        Assert.Equal("temp", revived.Name);
    }

    [Fact]
    public void Snapshot_GuideBody_FallsBackToDescription_WhenBodyEmpty()
    {
        _service.AddGuide("oneliner", "just the description", "   ");
        Assert.Equal("just the description", _service.Snapshot().GuideBody("oneliner"));
    }

    [Fact]
    public void Snapshot_GuideBody_NullForUnknownName()
    {
        Assert.Null(_service.Snapshot().GuideBody("nope"));
    }

    // MARK: - Row merge

    [Fact]
    public void RowsForTools_MergesOverride()
    {
        _service.SetOverride("a", "my a");
        var rows = _service.RowsForTools(new[]
        {
            new AiGuideToolInput("a", "default a", "Foundational"),
            new AiGuideToolInput("b", "default b", "Search"),
        });

        var a = Assert.Single(rows, r => r.Name == "a");
        Assert.True(a.IsOverridden);
        Assert.Equal("my a", a.EffectiveDescription);

        var b = Assert.Single(rows, r => r.Name == "b");
        Assert.False(b.IsOverridden);
        Assert.Equal("default b", b.EffectiveDescription);
    }

    // MARK: - Slug unit

    [Theory]
    [InlineData("Hello World", "hello_world")]
    [InlineData("__weird--name..", "weird_name")]
    [InlineData("café résumé", "caf_rsum")]
    [InlineData("ALLCAPS", "allcaps")]
    public void Slug_Sanitizes(string input, string expected)
        => Assert.Equal(expected, AiGuideService.Slug(input));
}
