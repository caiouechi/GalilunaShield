using GalilunaShield;

namespace GalilunaShield.App;

/// <summary>Wraps an alert with the parent's triage state and the actions they can take on it.</summary>
public sealed class AlertItemViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private AlertStatus _status;

    public AlertItemViewModel(ShellViewModel shell, AlertRecord record, AlertStatus status)
    {
        _shell = shell;
        Record = record;
        _status = status;
        ReviewCommand = new RelayCommand(() => _shell.SetAlertStatus(this, AlertStatus.Reviewed));
        DismissCommand = new RelayCommand(() => _shell.SetAlertStatus(this, AlertStatus.Dismissed));
        MuteWordCommand = new RelayCommand(() => _shell.MuteWord(PrimaryWord, this));
        MuteAppCommand = new RelayCommand(() => _shell.MuteApp(AppName!, this), () => AppName is not null);
        PlayClipCommand = new RelayCommand(() => _shell.PlayClipCommand.Execute(Record.ClipPath));
        OpenDetailsCommand = new RelayCommand(() => _shell.OpenFileCommand.Execute(Record.DetailsPath));
        OpenScreenshotCommand = new RelayCommand(() => _shell.OpenFileCommand.Execute(Record.ScreenshotPath));
        RevealCommand = new RelayCommand(() => _shell.RevealFileCommand.Execute(Record.ClipPath ?? Record.DetailsPath));
    }

    public AlertRecord Record { get; }

    public AlertStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsNew));
                OnPropertyChanged(nameof(IsDismissed));
                OnPropertyChanged(nameof(IsReviewed));
            }
        }
    }

    public bool IsNew => _status == AlertStatus.New;
    public bool IsReviewed => _status == AlertStatus.Reviewed;
    public bool IsDismissed => _status == AlertStatus.Dismissed;
    public string StatusText => _status switch
    {
        AlertStatus.Reviewed => "Reviewed",
        AlertStatus.Dismissed => "Not a concern",
        _ => "New",
    };

    // Convenience pass-throughs for binding
    public DateTimeOffset At => Record.At;
    public Severity Severity => Record.Severity;
    public bool PossibleOnly => Record.PossibleOnly;
    public string Text => Record.Text;
    public float Confidence => Record.Confidence;
    public string? HeardWhere => Record.HeardWhere;
    public IReadOnlyList<AlertMatchRecord> Matches => Record.Matches;
    public string? ClipPath => Record.ClipPath;
    public string? DetailsPath => Record.DetailsPath;
    public string? ScreenshotPath => Record.ScreenshotPath;

    public string PrimaryWord => Record.Matches.Count > 0 ? Record.Matches[0].Word : "";
    public string? AppName => MonitorService.AppNameOf(Record.Source);
    public string MuteAppText => AppName is null ? "Mute this app" : $"Mute {AppName}";

    public RelayCommand ReviewCommand { get; }
    public RelayCommand DismissCommand { get; }
    public RelayCommand MuteWordCommand { get; }
    public RelayCommand MuteAppCommand { get; }
    public RelayCommand PlayClipCommand { get; }
    public RelayCommand OpenDetailsCommand { get; }
    public RelayCommand OpenScreenshotCommand { get; }
    public RelayCommand RevealCommand { get; }
}
