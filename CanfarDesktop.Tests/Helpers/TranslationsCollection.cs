using Xunit;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// Tests that set a process-wide translation hook — <c>CutoutRules.Translate</c>,
/// <c>ObservationSummary.Translate</c>, <c>MarkSummary.Translate</c>, <c>ActivitySummary.Translate</c>.
/// They run on their own, after the rest: while one has a hook set, a test beside it that reads the
/// English would read the translation, and fail now and then for no reason of its own.
/// </summary>
[CollectionDefinition("Translations", DisableParallelization = true)]
public sealed class TranslationsCollection;
