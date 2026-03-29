namespace PomogatorLauncher;

internal enum TelegramBypassMode
{
    None = 0,
    /// <summary>Локальный SOCKS5 <see cref="TgWsProxyHost"/> — запросы к t.me через 127.0.0.1:1080.</summary>
    TgWsProxy = 1,
    /// <summary>Сборка <see href="https://github.com/IMROVICH/zapret-telegram"/> — запуск general.bat, трафик без SOCKS в приложении.</summary>
    ZapretTelegram = 2
}
