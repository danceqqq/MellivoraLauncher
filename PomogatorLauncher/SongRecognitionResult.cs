namespace PomogatorLauncher;

internal readonly struct SongRecognitionResult
{
    public bool IsSuccess { get; }
    public string Title { get; }
    public string Artist { get; }
    public string? CoverUrl { get; }
    public string? Error { get; }

    private SongRecognitionResult(bool success, string title, string artist, string? coverUrl, string? error)
    {
        IsSuccess = success;
        Title = title;
        Artist = artist;
        CoverUrl = coverUrl;
        Error = error;
    }

    internal static SongRecognitionResult Success(string title, string artist, string? coverUrl) =>
        new(true, title, artist, coverUrl, null);

    internal static SongRecognitionResult Fail(string? message) =>
        new(false, "", "", null, message);
}
