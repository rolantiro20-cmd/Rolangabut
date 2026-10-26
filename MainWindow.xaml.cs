using System.IO;
using System.Windows;
using System.Windows.Animation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly ImageBrush _artBrush = new() { Stretch = Stretch.UniformToFill };
    private readonly Brush _emptyArt = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));

    private string _lastTrackKey = "";
    private bool _hasArt;
    private bool _expanded;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;

        _progressTimer.Tick += (_, _) => UpdateProgress();
        _progressTimer.Start();
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            SetExpanded(false);
        };

        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => Dispatcher.InvokeAsync(AttachSession);
            AttachSession();
        }
        catch
        {
            CompactText.Text = "Media tidak tersedia";
        }
    }

    // ---------- Sesi media ----------

    private void AttachSession()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaChanged;
            _session.PlaybackInfoChanged -= OnPlaybackChanged;
        }

        _session = _manager?.GetCurrentSession();

        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnMediaChanged;
            _session.PlaybackInfoChanged += OnPlaybackChanged;
        }

        _lastTrackKey = "";
        _ = RefreshMediaAsync();
        RefreshPlayback();
    }

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs a)
        => Dispatcher.InvokeAsync(() => { _ = RefreshMediaAsync(); });

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs a)
        => Dispatcher.InvokeAsync(RefreshPlayback);

    private async Task RefreshMediaAsync()
    {
        var s = _session;
        if (s == null)
        {
            CompactText.Text = "Tidak ada musik";
            TitleText.Text = "Tidak ada musik";
            ArtistText.Text = "Putar lagu di Spotify, YouTube, dll.";
            ClearArt();
            _lastTrackKey = "";
            return;
        }

        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if (s != _session) return;

            var title = string.IsNullOrWhiteSpace(p.Title) ? "Memutar..." : p.Title;
            CompactText.Text = title;
            TitleText.Text = title;
            ArtistText.Text = p.Artist ?? "";

            var key = $"{p.Artist}|{p.Title}";
            if (key == _lastTrackKey && _hasArt) return;
            _lastTrackKey = key;

            if (p.Thumbnail == null)
            {
                ClearArt();
                return;
            }

            using var rs = await p.Thumbnail.OpenReadAsync();
            using var stream = rs.AsStreamForRead();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            if (s != _session) return;
            _artBrush.ImageSource = bmp;
            ArtSmall.Background = _artBrush;
            ArtBig.Background = _artBrush;
            _hasArt = true;
        }
        catch
        {
            ClearArt();
        }
    }

    private void ClearArt()
    {
        _hasArt = false;
        _artBrush.ImageSource = null;
        ArtSmall.Background = _emptyArt;
        ArtBig.Background = _emptyArt;
    }

    private bool IsPlaying()
    {
        try
        {
            return _session?.GetPlaybackInfo()?.PlaybackStatus ==
                   GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch { return false; }
    }

    private void RefreshPlayback()
    {
        PlayPauseButton.Content = IsPlaying() ? "\uE769" : "\uE768";
    }

    private void UpdateProgress()
    {
        try
        {
            var tl = _session?.GetTimelineProperties();
            if (tl == null) { Progress.Value = 0; return; }

            var total = (tl.EndTime - tl.StartTime).TotalSeconds;
            if (total <= 0) { Progress.Value = 0; return; }

            var pos = (tl.Position - tl.StartTime).TotalSeconds;
            if (IsPlaying())
                pos += (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds;

            Progress.Maximum = total;
            Progress.Value = Math.Clamp(pos, 0, total);
        }
        catch
        {
            Progress.Value = 0;
        }
    }

    // ---------- Tombol ----------

    private async void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_session != null) await _session.TrySkipPreviousAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_session != null) await _session.TryTogglePlayPauseAsync();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_session != null) await _session.TrySkipNextAsync();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    // ---------- Animasi island ----------

    private void Island_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer.Stop();
        SetExpanded(true);
    }

    private void Island_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer.Start();
    }

    private void Island_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Island.CornerRadius = new CornerRadius(Math.Min(28, Island.ActualHeight / 2));
    }

    private void SetExpanded(bool expand)
    {
        if (_expanded == expand) return;
        _expanded = expand;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(300);

        Island.BeginAnimation(WidthProperty,
            new DoubleAnimation(expand ? 420 : 190, dur) { EasingFunction = ease });
        Island.BeginAnimation(HeightProperty,
            new DoubleAnimation(expand ? 130 : 34, dur) { EasingFunction = ease });

        ExpandedView.IsHitTestVisible = expand;
        ExpandedView.BeginAnimation(OpacityProperty,
            new DoubleAnimation(expand ? 1 : 0, TimeSpan.FromMilliseconds(expand ? 250 : 120))
            { BeginTime = TimeSpan.FromMilliseconds(expand ? 120 : 0) });
        CompactView.BeginAnimation(OpacityProperty,
            new DoubleAnimation(expand ? 0 : 1, TimeSpan.FromMilliseconds(150))
            { BeginTime = TimeSpan.FromMilliseconds(expand ? 0 : 150) });
    }
}
