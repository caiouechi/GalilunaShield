using System.Collections.ObjectModel;
using GalilunaShield.Configuration;

namespace GalilunaShield.App;

/// <summary>One optional topic pack the parent can switch on with a checkbox.</summary>
public sealed class BucketItem : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly RedFlagBucket _bucket;
    private bool _enabled;

    public BucketItem(ShellViewModel shell, RedFlagBucket bucket, bool enabled)
    {
        _shell = shell;
        _bucket = bucket;
        _enabled = enabled;
    }

    public string Id => _bucket.Id;
    public string Name => _bucket.Name;
    public string Description => _bucket.Description;
    public int Count => _bucket.Count;
    public string CountText => $"{_bucket.Count} words";
    public bool Custom => !_bucket.Shipped;
    public string Path => _bucket.Path;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            if (_shell.SetBucketEnabled(_bucket.Id, value))
            {
                _enabled = value;
            }
            OnPropertyChanged(); // reverts the checkbox if the PIN was refused
        }
    }
}

/// <summary>The list of optional topic packs, shown on its own page.</summary>
public sealed class BucketsViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public BucketsViewModel(ShellViewModel shell)
    {
        _shell = shell;
        OpenFolderCommand = new RelayCommand(() => Shell.Open(AppPaths.UserBucketsDirectory));
        RefreshCommand = new RelayCommand(Refresh);
    }

    public ObservableCollection<BucketItem> Items { get; } = new();

    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand RefreshCommand { get; }

    private string _summary = "";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public void Refresh()
    {
        Items.Clear();
        foreach (var b in _shell.AllBuckets)
        {
            Items.Add(new BucketItem(_shell, b, _shell.IsBucketEnabled(b.Id)));
        }
        var on = Items.Count(i => i.Enabled);
        Summary = Items.Count == 0
            ? "No topic packs found."
            : $"{on} of {Items.Count} topic packs on. Ticking one adds its words to your child's protection immediately.";
    }
}
