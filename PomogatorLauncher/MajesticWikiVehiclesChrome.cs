namespace PomogatorLauncher;

/// <summary>
/// Скрипты для страниц Majestic Wiki: скрытие верхней панели (логотип, поиск, тема, язык, профиль).
/// </summary>
internal static class MajesticWikiVehiclesChrome
{
    internal const string VehiclesUrl = "https://wiki.majestic-rp.ru/ru/vehicles";
    internal const string ClothesMaleUrl = "https://wiki.majestic-rp.ru/ru/clothes/male";

    internal const string HideHeaderOnCreateScript = """
(function(){
  function kill(){
    try {
      document.querySelectorAll('div.ysuQKQR3, div[class*="ysuQKQR3"]').forEach(function(n){
        n.style.setProperty('display','none','important');
      });
      var inp = document.querySelector('input[placeholder*="Поиск информации"]');
      if (inp) {
        var p = inp;
        for (var i = 0; i < 24 && p; i++) {
          if (p.querySelector && p.querySelector('a[href="/ru"]')) {
            var r = p.getBoundingClientRect();
            if (r.top < 140) {
              p.style.setProperty('display','none','important');
              break;
            }
          }
          p = p.parentElement;
        }
      }
    } catch (e) {}
  }
  function boot(){
    var id = 'pomogator-wh-style';
    if (!document.getElementById(id)) {
      var s = document.createElement('style');
      s.id = id;
      s.textContent = 'div.ysuQKQR3,div[class*="ysuQKQR3"]{display:none!important;visibility:hidden!important;height:0!important;min-height:0!important;overflow:hidden!important;pointer-events:none!important}';
      (document.head || document.documentElement).appendChild(s);
    }
    kill();
    if (window.__pomogatorWikiObs) return;
    window.__pomogatorWikiObs = true;
    var t;
    new MutationObserver(function(){ clearTimeout(t); t = setTimeout(kill, 50); })
      .observe(document.documentElement, { childList: true, subtree: true });
  }
  if (document.readyState === 'loading')
    document.addEventListener('DOMContentLoaded', boot);
  else
    boot();
})();
""";

    internal const string HideHeaderKillOnlyScript = """
(function(){
  try {
    document.querySelectorAll('div.ysuQKQR3, div[class*="ysuQKQR3"]').forEach(function(n){
      n.style.setProperty('display','none','important');
    });
    var inp = document.querySelector('input[placeholder*="Поиск информации"]');
    if (inp) {
      var p = inp;
      for (var i = 0; i < 24 && p; i++) {
        if (p.querySelector && p.querySelector('a[href="/ru"]')) {
          var r = p.getBoundingClientRect();
          if (r.top < 140) {
            p.style.setProperty('display','none','important');
            break;
          }
        }
        p = p.parentElement;
      }
    }
  } catch (e) {}
})();
""";
}
