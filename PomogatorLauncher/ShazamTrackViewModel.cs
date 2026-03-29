using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PomogatorLauncher;

public sealed class ShazamTrackViewModel : INotifyPropertyChanged
{
    private static readonly HttpClient CoverHttp = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(20),
        DefaultRequestHeaders = { { "User-Agent", "PomogatorLauncher/1.0" } }
    };

    private ImageSource? _cover;

    public ShazamTrackViewModel(string title, string artist, string? coverUrl, DateTime recognizedAtUtc)
    {
        Title = title;
        Artist = artist;
        CoverUrl = coverUrl;
        RecognizedAtUtc = recognizedAtUtc;
        _ = LoadCoverAsync();
    }

    public string Title { get; }
    public string Artist { get; }
    public string? CoverUrl { get; }
    public DateTime RecognizedAtUtc { get; }

    public ImageSource? Cover
    {
        get => _cover;
        private set
        {
            if (ReferenceEquals(_cover, value)) return;
            _cover = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task LoadCoverAsync()
    {
        if (string.IsNullOrWhiteSpace(CoverUrl) || !Uri.TryCreate(CoverUrl, UriKind.Absolute, out var uri))
            return;

        try
        {
            var bytes = await CoverHttp.GetByteArrayAsync(uri).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.StreamSource = new MemoryStream(bytes);
                    img.DecodePixelWidth = 128;
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.EndInit();
                    img.Freeze();
                    Cover = img;
                }
                catch
                {
                    /* ignore */
                }
            });
        }
        catch
        {
            /* ignore */
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
