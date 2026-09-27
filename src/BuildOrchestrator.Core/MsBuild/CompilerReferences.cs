using System.Text.RegularExpressions;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>
/// [okunan dosya kanıtı] C# derleyicisinin MSBuild çıktısına yazdığı komut satırından <c>/reference:</c>
/// yollarını okur: bir derlemenin GERÇEKTE hangi dosyalara bağlandığının kesin kaydı budur. csproj'dan tahmin
/// (HintPath, ProjectReference, ReferencePath, koşullar, import'lar) MSBuild'in çözümünü taklit etmek olurdu;
/// bu satır çözümün SONUCUDUR. Satır yoksa (derleyici koşmadı — CoreCompile güncel bulunup atlandı — ya da çıktı
/// ayrıntısı düşürüldü) bilgi de yoktur; karar çağıranındır. <see cref="CopyContention"/> gibi yalnız MSBuild'in
/// ürettiği satırı okur, hiçbir şeyi değiştirmez.
/// </summary>
public static partial class CompilerReferences
{
    // Derleyici çağrısı: klasik csc.exe ya da SDK'nın dotnet üzerinden koşturduğu csc.dll; adın ardından boşluk
    // ya da kapanan tırnak gelir. "csc.exe" geçen bir tanı satırı (MSB6006) da eşleşir ama /reference:
    // taşımadığı için bilgi üretmez.
    [GeneratedRegex(@"\bcsc\.(?:exe|dll)[""\s]", RegexOptions.IgnoreCase)]
    private static partial Regex CompilerPattern();

    // /reference:yol ya da /reference:"boşluklu yol" — Csc görevi her referansı ayrı yazar.
    [GeneratedRegex(@"(?<=\s)/reference:(?:""(?<path>[^""]+)""|(?<path>[^\s""]+))", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePattern();

    /// <summary>Satır bir C# derleyici çağrısıysa referans yollarını satırdaki sırayla döner; değilse ya da hiç
    /// referans taşımıyorsa <c>null</c> — bilgi yok (boş liste "hiçbir şey okumadı" iddiası olurdu).</summary>
    public static IReadOnlyList<string>? Parse(string line)
    {
        if (string.IsNullOrEmpty(line) || !CompilerPattern().IsMatch(line)) return null;
        var references = new List<string>();
        foreach (Match match in ReferencePattern().Matches(line))
            references.Add(match.Groups["path"].Value);
        return references.Count > 0 ? references : null;
    }
}
