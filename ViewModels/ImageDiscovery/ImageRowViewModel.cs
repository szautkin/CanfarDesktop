using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CanfarDesktop.Helpers.ImageDiscovery;
using CanfarDesktop.Models;
using CanfarDesktop.Models.ImageDiscovery;

namespace CanfarDesktop.ViewModels.ImageDiscovery;

public enum RowStateKind { NeverDiscovered, Running, Discovered, Failed }

/// <summary>Per-image discovery state for the right pane (mirrors macOS <c>RowState</c>).</summary>
public sealed class RowState
{
    public RowStateKind Kind { get; init; }
    public ImageManifest? Manifest { get; init; }
    public FailureCategory? Category { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset AttemptedAt { get; init; }
    public string? JobID { get; init; }

    public static readonly RowState NeverDiscovered = new() { Kind = RowStateKind.NeverDiscovered };
    public static RowState Running() => new() { Kind = RowStateKind.Running };

    public static RowState FromOutcome(LastOutcome? o)
    {
        if (o is null) return NeverDiscovered;
        if (o.IsSuccess && o.Manifest is { } m)
            return new RowState { Kind = RowStateKind.Discovered, Manifest = m, AttemptedAt = o.AttemptedAt };
        return new RowState
        {
            Kind = RowStateKind.Failed,
            Category = o.Category,
            Message = o.Message,
            AttemptedAt = o.AttemptedAt,
            JobID = o.JobID,
        };
    }
}

/// <summary>One image row in the right (matching-images) pane.</summary>
public partial class ImageRowViewModel : ObservableObject
{
    public ParsedImage Image { get; }
    public string Id => Image.Id;
    public string Label => Image.Label;

    [ObservableProperty] private RowState _state;
    [ObservableProperty] private bool _isSelected;

    private readonly Func<ImageRowViewModel, bool, Task>? _discover;
    private readonly Action<ImageRowViewModel>? _dismiss;

    public ImageRowViewModel(ParsedImage image, RowState state,
        Func<ImageRowViewModel, bool, Task>? discover = null,
        Action<ImageRowViewModel>? dismiss = null)
    {
        Image = image;
        _state = state;
        _discover = discover;
        _dismiss = dismiss;
    }

    [RelayCommand] private Task Discover() => _discover?.Invoke(this, false) ?? Task.CompletedTask;
    [RelayCommand] private Task Rediscover() => _discover?.Invoke(this, true) ?? Task.CompletedTask;
    [RelayCommand] private void Dismiss() => _dismiss?.Invoke(this);

    public bool IsDiscovered => State.Kind == RowStateKind.Discovered;
    public bool IsRunning => State.Kind == RowStateKind.Running;
    public bool IsFailed => State.Kind == RowStateKind.Failed;
    public bool HasJobLogs => State.Kind == RowStateKind.Failed && !string.IsNullOrEmpty(State.JobID);

    public int PackageCount => State.Manifest is { } m ? DiscoveryFormatting.PackageCount(m) : 0;

    public string StatusText => State.Kind switch
    {
        RowStateKind.Discovered => $"{PackageCount} packages",
        RowStateKind.Running => "Discovering…",
        RowStateKind.Failed => DiscoveryFormatting.CategoryLabel(State.Category ?? FailureCategory.Unknown),
        _ => "Not inspected",
    };

    public string DetailLine => State.Kind switch
    {
        RowStateKind.Discovered => $"{PackageCount} packages · {DiscoveryFormatting.TimeAgo(State.AttemptedAt)}",
        RowStateKind.Failed => $"{DiscoveryFormatting.CategoryLabel(State.Category ?? FailureCategory.Unknown)} · {DiscoveryFormatting.TimeAgo(State.AttemptedAt)}",
        RowStateKind.Running => "Inspecting image…",
        _ => "Not inspected yet",
    };

    public string? JobIdLine => HasJobLogs ? $"Job {State.JobID}" : null;

    partial void OnStateChanged(RowState value)
    {
        OnPropertyChanged(nameof(IsDiscovered));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(HasJobLogs));
        OnPropertyChanged(nameof(PackageCount));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DetailLine));
        OnPropertyChanged(nameof(JobIdLine));
    }
}

/// <summary>A project section of image rows for the grouped right-pane list.</summary>
public sealed class ImageRowGroup
{
    public string Project { get; }
    public ObservableCollection<ImageRowViewModel> Images { get; }

    public ImageRowGroup(string project, IEnumerable<ImageRowViewModel> images)
    {
        Project = project;
        Images = new ObservableCollection<ImageRowViewModel>(images);
    }
}
