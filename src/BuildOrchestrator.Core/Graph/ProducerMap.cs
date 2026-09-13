namespace BuildOrchestrator.Core.Graph;

using BuildOrchestrator.Core.Discovery;

/// <summary>
/// DLL adı (lower, ör. "osys.b.dll") → onu üreten proje Id'si eşlemesi.
/// </summary>
/// <param name="DllToProducer">Tek üreticisi olan DLL'ler — kenarların kaynağı.</param>
/// <param name="AmbiguousProducers">Birden fazla proje aynı <c>AssemblyName</c>'i üretiyorsa DLL adı → o
/// projelerin yolları (determinist sıra). Bu DLL'ler <see cref="DllToProducer"/>'dan ÇIKARILIR: belirsiz bir
/// DLL kenar üretmez [D8/D11] — hangi projeyi seçeceğimizi bilemeyiz ve yanlış seçmek yanlış bir build sırası
/// demektir.
/// <para><b>Üreticiler de taşınır, yalnız DLL adı değil.</b> Kenarın düşmesi SESSİZ bir kayıptır: o DLL'e
/// HintPath ile bağlanan her proje bağımlılığını yitirir. Kullanıcının tek çözümü çakışan adlardan birini
/// değiştirmek ya da köklerden birini listeden çıkarmaktır, bunun için de HANGİ projelerin çakıştığını
/// görmesi gerekir (bkz. <c>PlanProgressLines.AmbiguousProducer</c>).</para></param>
public sealed record ProducerMap(
    IReadOnlyDictionary<string, string> DllToProducer,
    IReadOnlyDictionary<string, IReadOnlyList<string>> AmbiguousProducers)
{
    /// <summary>Belirsiz DLL adları — <see cref="AmbiguousProducers"/>'dan TÜRETİLİR (ikinci bir liste
    /// tutulmaz; kopya YASAK).</summary>
    public IReadOnlyList<string> AmbiguousDlls => [.. AmbiguousProducers.Keys];
}

/// <summary>
/// Projelerin AssemblyName'inden producer map çıkarır. MSBuild çalıştırılmaz;
/// yalnız <see cref="EvaluatedProject.AssemblyName"/> kullanılır [Global Constraints raw-XML].
/// </summary>
public static class ProducerMapBuilder
{
    public static ProducerMap Build(IReadOnlyList<EvaluatedProject> projects)
    {
        // Determinizm [D8]: proje sırası OrdinalIgnoreCase Path'e göre.
        var multi = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase))
        {
            string dll = p.AssemblyName.ToLowerInvariant() + ".dll";
            (multi.TryGetValue(dll, out var l) ? l : multi[dll] = new()).Add(p.Path);
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Sıralı sözlük: hem anahtar sırası hem üretici sırası determinist olmalı (uyarı metni de buradan doğar).
        var ambiguous = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dll, producers) in multi.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (producers.Count == 1) map[dll] = producers[0];
            else ambiguous[dll] = producers; // determinizm: belirsizi kenar yapma [D8/D11]
        }

        return new ProducerMap(map, ambiguous);
    }
}
