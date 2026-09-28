using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E2/T16 · P4] <see cref="IAutostartRegistry"/>'nin bellek-içi eşi — testler GERÇEK <c>HKCU\...\Run</c>'a ASLA
/// yazmaz. Tek yer (kopya YASAK): <see cref="AutostartServiceTests"/>, <see cref="StartWithWindowsTests"/> ve
/// <see cref="SettingsDialogHost"/> bunu paylaşır.
/// </summary>
internal sealed class FakeAutostartRegistry : IAutostartRegistry
{
    /// <summary>Testlerin kullandığı Run komutu — üretimdeki biçimle aynı: tırnaklı exe yolu + autostart argümanı.</summary>
    public const string Command = "\"C:\\app\\BuildOrchestrator.App.exe\" --autostart";

    private readonly Dictionary<string, string> _run = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Görev Yöneticisi → Başlangıç uygulamaları'nda "devre dışı" işaretli değerler
    /// (<c>HKCU\...\Explorer\StartupApproved\Run</c>).</summary>
    private readonly HashSet<string> _disabledInStartupApps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Verildiyse her YAZIM bunu fırlatır — Windows'un kaydı reddettiği durum (ör. bir politika ya da
    /// güvenlik yazılımı <c>HKCU\...\Run</c>'ı kilitlemiş).</summary>
    public Exception? FailWritesWith { get; set; }

    /// <summary>Başarılı yazım sayısı — "Windows kaydına dokunulmadı" iddiası için.</summary>
    public int Writes { get; private set; }

    /// <summary>Bu sahte kaydı süren servis — üretimdeki değer adıyla.</summary>
    public AutostartService Service() => new(this, AutostartService.DefaultValueName, Command);

    public void Set(string name, string command)
    {
        Write();
        _run[name] = command;
    }

    public void Remove(string name)
    {
        Write();
        _run.Remove(name);
    }

    public bool Exists(string name) => _run.ContainsKey(name);

    public string? CommandFor(string name) => _run.TryGetValue(name, out var v) ? v : null;

    public bool IsStartupDisabled(string name) => _disabledInStartupApps.Contains(name);

    public void ClearStartupDisabled(string name)
    {
        Write();
        _disabledInStartupApps.Remove(name);
    }

    /// <summary>Kullanıcının Görev Yöneticisi'nde "Devre dışı bırak"a basması — Windows'un kendi yazımıdır, bu yüzden
    /// <see cref="Writes"/>'a sayılmaz.</summary>
    public void DisableInStartupApps(string name) => _disabledInStartupApps.Add(name);

    private void Write()
    {
        if (FailWritesWith is { } ex) throw ex;
        Writes++;
    }
}
