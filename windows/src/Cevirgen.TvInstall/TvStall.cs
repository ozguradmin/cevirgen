// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.TvInstall;

/// <summary>Kurulumun bir adımı süresinde bitmedi: TV yanıt vermeyi kesti, bağlantı kapatıldı.</summary>
public sealed class TvStepTimeoutException(string step, TimeSpan limit)
    : TimeoutException($"TV \"{TvStall.StepName(step)}\" adımında {limit.TotalSeconds:0} sn yanıt vermedi")
{
    /// <summary>Takılan adım ("installing", "launching" …).</summary>
    public string Step { get; } = step;

    public TimeSpan Limit { get; } = limit;
}

/// <summary>
/// TV uygulaması güncellemesinin bekçisi: her adımın (bağlanma, sürümü okuma, eski uygulamayı kapatma, kurulum, açma)
/// süre sınırı vardır; süre dolunca TV bağlantısı kapatılır ve adım <see cref="TvStepTimeoutException"/> ile biter.
/// <para>
/// Önceden masaüstündeki Samsung (sdb) ve Android TV (ADB) kurulumunun hiçbir adımında süre sınırı yoktu: TizenSdb'nin
/// kurulumu (<c>TizenInstaller.InstallApp</c>) iptal belirteci bile almıyor, kabuk komutları ve ADB akışları TV yanıt
/// vermeyi kesince sonsuza dek bekliyordu. Güncelleme kartı "kuruluyor"da kalıyor, Mac'te yardımcı hiç bitmiyordu;
/// kullanıcı TV'yi yeniden başlatmak zorunda kalıyordu. iPhone (TVStall) ve Android (TvStall) aynı adımları ve süreleri
/// kullanır.
/// </para>
/// </summary>
public sealed class TvStall : IDisposable
{
    /// <summary>Takılma sayılınca işin kendiliğinden bitmesi için tanınan ek süre (sonra bırakılır).</summary>
    internal static TimeSpan Grace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Adımların süre sınırı. Kullanıcının bir şey yaptığı adımlarda (Samsung girişi, TV'deki Play sayfası) sınır yoktur.
    /// Android TV'de bağlanma adımı TV'deki onay sorusunu da (en çok 90 sn) içerir.
    /// </summary>
    public static TimeSpan? LimitFor(string step) => step switch
    {
        "connecting" => TimeSpan.FromSeconds(45),
        "approve" => TimeSpan.FromSeconds(150),
        "reading" => TimeSpan.FromSeconds(60),
        "signin" or "play" => null,
        "certificate" => TimeSpan.FromSeconds(240),
        "signing" => TimeSpan.FromSeconds(120),
        "closing" => TimeSpan.FromSeconds(30),
        "installing" => TimeSpan.FromSeconds(360),
        "uninstalling" => TimeSpan.FromSeconds(120),
        "launching" => TimeSpan.FromSeconds(60),
        _ => TimeSpan.FromSeconds(120),
    };

    /// <summary>Adımın günlükte ve hata ayrıntısında görünen adı.</summary>
    public static string StepName(string step) => step switch
    {
        "connecting" => "bağlanma",
        "approve" => "TV'de onay",
        "reading" => "TV'deki sürümü okuma",
        "certificate" => "sertifika",
        "signing" => "paket imzalama",
        "closing" => "eski uygulamayı kapatma",
        "installing" => "paket gönderme ve kurulum",
        "uninstalling" => "eski kopyayı kaldırma",
        "launching" => "uygulamayı açma",
        _ => step,
    };

    private readonly object gate = new();
    private readonly Func<string, TimeSpan?> limits;
    private readonly CancellationTokenSource stall = new();
    private readonly CancellationTokenSource linked;
    private readonly TaskCompletionSource stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Action> closers = [];
    private string step = "";
    private TimeSpan limit;

    /// <param name="cancellationToken">Dışarıdan iptal (kullanıcı vazgeçti); takılma sayılmaz.</param>
    /// <param name="limits">Testler için adım süreleri; verilmezse <see cref="LimitFor"/>.</param>
    public TvStall(CancellationToken cancellationToken, Func<string, TimeSpan?>? limits = null)
    {
        this.limits = limits ?? LimitFor;
        linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stall.Token);
        stall.Token.Register(Fire);
        Enter("connecting");
    }

    /// <summary>Adımların kullandığı belirteç: takılınca ya da dışarıdan iptal edilince iptal olur.</summary>
    public CancellationToken Token => linked.Token;

    /// <summary>Takılan adım; takılma yoksa null.</summary>
    public string? StalledStep { get; private set; }

    public bool Stalled => StalledStep is not null;

    /// <summary>Yeni adıma geçildi: süre baştan başlar (sınırsız adımda süre durur).</summary>
    public void Enter(string next)
    {
        lock (gate)
        {
            if (Stalled) return;
            step = next;
            if (limits(next) is { } value)
            {
                limit = value;
                stall.CancelAfter(value);
            }
            else
            {
                stall.CancelAfter(Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>Takılınca çalışacak kapatma (TV bağlantısı); bekleyen okuma/yazma böylece hatayla biter.</summary>
    public void OnStall(Action close)
    {
        lock (gate)
        {
            if (!Stalled)
            {
                closers.Add(close);
                return;
            }
        }
        Quietly(close);
    }

    /// <summary>Takılan adımın hatası.</summary>
    public TvStepTimeoutException Error() => new(StalledStep ?? step, limit);

    private void Fire()
    {
        Action[] close;
        lock (gate)
        {
            StalledStep = step;
            close = [.. closers];
        }
        foreach (var action in close) Quietly(action);
        stalled.TrySetResult();
    }

    private static void Quietly(Action action)
    {
        try { action(); }
        catch (Exception) { /* bağlantı zaten kapanmış olabilir */ }
    }

    /// <summary>
    /// Denemeyi bekçiyle çalıştırır. Adımlardan biri süresini aşarsa bağlantı kapatılır; deneme <see cref="Grace"/> içinde
    /// bitmezse bırakılır (iptal belirteci almayan TizenSdb kurulumu). Sonuç her durumda <see cref="TvStepTimeoutException"/>
    /// olur: takılmış bağlantının yuttuğu hata ("kurulum tamam" ama uygulama açılamadı) da sessiz geçmez.
    /// </summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> attempt)
    {
        Task<T> work;
        try
        {
            work = attempt(Token);
        }
        catch (Exception) when (Stalled)
        {
            throw Error();
        }
        if (await Task.WhenAny(work, stalled.Task) != work)
        {
            await Task.WhenAny(work, Task.Delay(Grace));
            _ = work.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            throw Error();
        }
        try
        {
            var result = await work;
            if (Stalled) throw Error();
            return result;
        }
        catch (Exception error) when (Stalled && error is not TvStepTimeoutException)
        {
            throw Error();
        }
    }

    /// <summary>
    /// Denemeyi bir kez daha yapar: ilk deneme takıldıysa ya da bağlantı koptuysa (<paramref name="retryable"/>) günlüğe
    /// yazılır ve yeni bağlantıyla baştan denenir. İkinci denemenin hatası olduğu gibi yükselir.
    /// </summary>
    public static async Task<T> RetryOnceAsync<T>(Func<int, Task<T>> attempt, Func<Exception, bool> retryable, Action<string>? log)
    {
        try
        {
            return await attempt(1);
        }
        catch (Exception error) when (retryable(error))
        {
            log?.Invoke($"Takıldı: {error.Message}; bağlantı kapatıldı, bir kez daha deneniyor.");
        }
        return await attempt(2);
    }

    /// <summary>İkinci denemede de takılınca kullanıcıya gösterilen ayrıntı.</summary>
    public static string GaveUp(TvStepTimeoutException error) =>
        $"{error.Message}; bir kez daha denendi, TV yine yanıt vermedi";

    public void Dispose()
    {
        lock (gate)
        {
            stall.Dispose();
            linked.Dispose();
        }
    }
}
