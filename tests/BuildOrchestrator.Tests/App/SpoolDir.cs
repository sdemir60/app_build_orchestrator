using System.IO;
using System.Text;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.Core.Logs;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 2 · spec §6.3] Anlatı spool'unu açan testlerin geçici koşu klasörü — TEK yer (kopya YASAK): store testleri,
/// görünüm testi, VM testleri ve bellek bütçesi aynı klasörü ve aynı okumayı kullanır.
///
/// <para><b>Ömür:</b> <see cref="Dispose"/> önce store'u temizler — spool'u kapatır (<see cref="NarrativeStore.Clear"/>) — sonra
/// klasörü siler. Açık bir yazıcı klasörün silinmesini engeller ve <see cref="TempDir"/> o hatayı yutar: tutamaç da klasör de sessizce
/// sızardı. Store'u yeni bir işlem çoktan temizlediyse ikinci temizlik zararsızdır.</para>
/// </summary>
internal sealed class SpoolDir(NarrativeStore store) : IDisposable
{
    private readonly TempDir _dir = new();

    /// <summary>Koşu klasörü — <c>RunStartedEvent.LogDirectory</c>'nin test karşılığı (var olan, tam nitelikli bir klasör).</summary>
    public string Path => _dir.Path;

    /// <summary>Klasördeki anlatı dosyası — adı üretimin TEK tanımından (<see cref="RunLogPaths.NarrativeFileName"/>).</summary>
    public string FilePath => System.IO.Path.Combine(_dir.Path, RunLogPaths.NarrativeFileName);

    /// <summary>Anlatı dosyasının ham metni. Yazıcı açıkken de okunur: yazıcının yazma erişimi paylaşım kipi
    /// <see cref="FileShare.ReadWrite"/> olmayan bir okumayı reddeder (<c>File.ReadAllText</c> IOException verirdi). Baytlar olduğu gibi
    /// çözülür — bir BOM ayıklanmaz, bir '\r' kaybolmaz: dosyanın biçimini sınayan testler onları görür.</summary>
    public string ReadText()
    {
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    public void Dispose()
    {
        store.Clear();
        _dir.Dispose();
    }
}
