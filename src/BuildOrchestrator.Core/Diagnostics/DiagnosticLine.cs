namespace BuildOrchestrator.Core.Diagnostics;

/// <summary>
/// [son temizlik · madde 13 · O-2] Bir tanı satırının TEK güvenli yazımı: satır kurulur ve kanala yazılır; kurulum (ör. süreç okuması) ya da
/// yazım (ör. kapanmış stderr borusu) patlarsa hata YUTULUR — bir tanı satırı taşıdığı cevabı ya da işi (Sync, koşu kapanışı, "log yok" cevabı)
/// asla bozmamalıdır. "Yaz ve hatasını yut" deyimi yalnız burada yazılır (kopya YASAK); iki kullanıcısı: motorun bellek satırı
/// (<see cref="MemoryLine.Report"/>) ve koordinatörün cevap ile kapanış yollarındaki uyarıları (<c>RunCoordinator.ReportDiagnostic</c>).
/// </summary>
public static class DiagnosticLine
{
    /// <summary><paramref name="line"/>'ın kurduğu satırı <paramref name="sink"/>'e yazar; kurulum ya da yazım patlarsa sessizce vazgeçer.</summary>
    /// <param name="sink">Tanı kanalı (motorda stderr).</param>
    /// <param name="line">Satırı kurar — kurulumun hatası da yutulur (satırı okumayla kuran çağıran için).</param>
    public static void Write(Action<string> sink, Func<string> line)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(line);
        try { sink(line()); }
        catch (Exception) { /* tanı: kurulamadı ya da yazılamadı — sessizce vazgeç, cevabı ve işi bozma */ }
    }
}
