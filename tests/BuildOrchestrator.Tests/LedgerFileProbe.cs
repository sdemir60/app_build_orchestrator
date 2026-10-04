using System;
using System.IO;

namespace BuildOrchestrator.Tests;

/// <summary>
/// Defter dosyası (<c>EvaluationCache</c> ve <c>SourceHashCache</c>) testlerinin ORTAK yardımcısı (kopya YASAK): "yeniden
/// yazıldı mı" sondası ve dosyaya karşı paylaşım senaryoları. Sonda: dosyanın mtime'ı çok eski bir damgaya sabitlenir;
/// defter dosyayı yeniden yazarsa (temp + <c>File.Move</c> → dosya yer değiştirir) damga bugüne gelir. Saat ve uyku yok:
/// deterministik.
/// </summary>
internal sealed class LedgerFileProbe
{
    private static readonly DateTime PinnedUtc = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _path;

    private LedgerFileProbe(string path) => _path = path;

    /// <summary>Dosyanın mtime'ını eski damgaya çeker ve sondayı döner (dosya VAR olmalı).</summary>
    public static LedgerFileProbe Pin(string path)
    {
        File.SetLastWriteTimeUtc(path, PinnedUtc);
        return new LedgerFileProbe(path);
    }

    /// <summary>Sonda kurulduğundan beri dosya yeniden yazıldı (yer değiştirdi) mi.</summary>
    public bool WasRewritten => File.GetLastWriteTimeUtc(_path) != PinnedUtc;

    /// <summary>
    /// Dosyayı paylaşıma tamamen kapalı tutar (<c>FileShare.None</c>): yazıcının rename'i tutamak açıkken HİÇ geçemez.
    /// Başarısız yazım testlerinin ortak "hedefi kilitle" deyimi.
    /// </summary>
    public static IDisposable HoldLocked(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    /// <summary>
    /// DELETE erişimi tutan bir tutamak: <c>FileOptions.DeleteOnClose</c> açıkken Windows dosyaya SONRADAN açılan her handle'dan
    /// <c>FileShare.Delete</c> ister — eşzamanlı bir atomik rename'in hedefte istediği şeyin aynısı. Paylaşım denetimi
    /// simetriktir: "okuyucu önce açık, rename sonra" sırasındaki uyuşmazlık burada "tutamak önce açık, okuyucu sonra"
    /// sırasıyla gözlenir; okuyucunun açık kaldığı kısa pencereye girmek gerekmez (saat ve uyku yok).
    /// DİKKAT: Dispose dosyayı SİLER.
    /// </summary>
    public static IDisposable HoldDeleteAccess(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.DeleteOnClose);

    /// <summary>
    /// Hedefi Delete-share'siz tutar (<c>FileShare.Read</c>): yazıcının rename'i düşer ve retry'a girer. Tutamak,
    /// <see cref="RenameBlock.ReleaseOnRetry"/> retry gecikmesi dikişine bağlanınca İLK retry'da bırakılır — kısa süreli
    /// paylaşım ihlalinin deterministik modeli (BuildStateStore testlerinin randevusunun tek thread'li hâli).
    /// </summary>
    public static RenameBlock BlockRename(string path) => new(path);

    public sealed class RenameBlock : IDisposable
    {
        private readonly FileStream _holder;

        internal RenameBlock(string path) =>
            _holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        /// <summary>Retry'a kaç kez girildi (gecikme dikişi kaç kez çağrıldı).</summary>
        public int Retries { get; private set; }

        /// <summary><c>RenameRetryDelay</c> dikişi olarak verilir: retry'a ilk girişte tutamağı bırakır.</summary>
        public void ReleaseOnRetry(int attempt)
        {
            Retries++;
            _holder.Dispose();
        }

        public void Dispose() => _holder.Dispose();
    }
}
