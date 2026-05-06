using System.Media;
using System.Windows;
using CopilotSessionMonitor.Services;

namespace CopilotSessionMonitor.Views;

/// <summary>
/// Modal settings dialog. Acts on a copy of <see cref="AppSettings"/>; the
/// caller owns the canonical instance and applies the result to live
/// services on Save. Cancel discards.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly string[] s_soundNames =
    {
        nameof(NotificationSound.None),
        nameof(NotificationSound.Default),
        nameof(NotificationSound.Asterisk),
        nameof(NotificationSound.Beep),
        nameof(NotificationSound.Question),
        nameof(NotificationSound.Exclamation),
        nameof(NotificationSound.Hand),
    };

    private readonly AppSettings _draft;

    public AppSettings? Result { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        _draft = Clone(current);

        HeartbeatBox.Text = _draft.HeartbeatSeconds.ToString();
        StaleHoursBox.Text = _draft.StaleThresholdHours.ToString();
        NotifyBlueBox.IsChecked = _draft.NotifyOnBlue;
        NotifyRedToGreenBox.IsChecked = _draft.NotifyOnRedToGreen;

        BlueSoundBox.ItemsSource = s_soundNames;
        GreenSoundBox.ItemsSource = s_soundNames;
        BlueSoundBox.SelectedItem = MatchSoundName(_draft.BlueSound, "Asterisk");
        GreenSoundBox.SelectedItem = MatchSoundName(_draft.RedToGreenSound, "Default");
    }

    private static string MatchSoundName(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var hit = s_soundNames.FirstOrDefault(n => string.Equals(n, raw, StringComparison.OrdinalIgnoreCase));
        return hit ?? fallback;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(HeartbeatBox.Text, out var heartbeat) || heartbeat < 1 || heartbeat > 60)
        {
            System.Windows.MessageBox.Show(this, "Heartbeat interval must be a whole number from 1 to 60 seconds.",
                "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning);
            HeartbeatBox.Focus();
            return;
        }
        if (!int.TryParse(StaleHoursBox.Text, out var stale) || stale < 1 || stale > 168)
        {
            System.Windows.MessageBox.Show(this, "Stale threshold must be a whole number from 1 to 168 hours.",
                "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning);
            StaleHoursBox.Focus();
            return;
        }

        _draft.HeartbeatSeconds = heartbeat;
        _draft.StaleThresholdHours = stale;
        _draft.NotifyOnBlue = NotifyBlueBox.IsChecked == true;
        _draft.NotifyOnRedToGreen = NotifyRedToGreenBox.IsChecked == true;
        _draft.BlueSound = (BlueSoundBox.SelectedItem as string) ?? _draft.BlueSound;
        _draft.RedToGreenSound = (GreenSoundBox.SelectedItem as string) ?? _draft.RedToGreenSound;

        Result = _draft;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PreviewBlue_Click(object sender, RoutedEventArgs e) => Preview(BlueSoundBox.SelectedItem as string);
    private void PreviewGreen_Click(object sender, RoutedEventArgs e) => Preview(GreenSoundBox.SelectedItem as string);

    private static void Preview(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!Enum.TryParse<NotificationSound>(name, ignoreCase: true, out var s)) return;
        switch (s)
        {
            case NotificationSound.None: return;
            case NotificationSound.Default:
            case NotificationSound.Beep: SystemSounds.Beep.Play(); return;
            case NotificationSound.Asterisk: SystemSounds.Asterisk.Play(); return;
            case NotificationSound.Question: SystemSounds.Question.Play(); return;
            case NotificationSound.Exclamation: SystemSounds.Exclamation.Play(); return;
            case NotificationSound.Hand: SystemSounds.Hand.Play(); return;
        }
    }

    private static AppSettings Clone(AppSettings s) => new()
    {
        WindowWidth = s.WindowWidth,
        WindowHeight = s.WindowHeight,
        WindowLeft = s.WindowLeft,
        WindowTop = s.WindowTop,
        IsPinned = s.IsPinned,
        ShowOffline = s.ShowOffline,
        StaleThresholdHours = s.StaleThresholdHours,
        HeartbeatSeconds = s.HeartbeatSeconds,
        NotifyOnBlue = s.NotifyOnBlue,
        NotifyOnRedToGreen = s.NotifyOnRedToGreen,
        BlueSound = s.BlueSound,
        RedToGreenSound = s.RedToGreenSound,
    };
}
