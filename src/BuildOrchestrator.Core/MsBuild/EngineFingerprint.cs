using System.Diagnostics;
using System.Text;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>
/// [RESOLVE Faz 3 — karar 6] Motor parmak izi: bir döngü üyesinin kaydını yazan derlemenin HANGİ MSBuild ile ve hangi
/// build komut satırı sözleşmesiyle yapıldığı. <see cref="Contracts.Model.BuildState.CycleEngineFingerprint"/>'a
/// yazılır; Resolve'un tur 1'inde bu koşununkinden farklıysa grupta herkes gerekli sayılır (Visual Studio güncellendi,
/// başka bir MSBuild seçildi ya da argüman sözleşmesi — ör. WPF targets yolu — değişti).
///
/// <para><b>Girenler:</b> MSBuild.exe tam yolu, dosya sürümü ve <see cref="MsBuildArguments.Build"/> çıktısının
/// proje yolu DIŞINDAKİ öğeleri, sırasıyla. Liste sabit yer tutucularla istenir: ilk öğe (proje yolu) atılır,
/// configuration DEĞERİ yer tutucu olarak girer — ikisi de üyenin kendi teriminde zaten vardır. WPF targets argümanı
/// (<c>-p:CustomBeforeMicrosoftCommonTargets=</c>) listenin parçasıdır; parmak izi onu da kapsar.</para>
///
/// <para><b>Girmeyen: restore çağrısı.</b> Build'den önce koşabilen ayrı <c>-t:restore</c> çağrısı (<see
/// cref="MsBuildArguments.RestorePackagesConfig"/>) derlemez ve koşup koşmayacağı her koşuda kanıta göre değişir (<see
/// cref="RestoreEvidence"/>); parmak izine girseydi aynı motor koşudan koşuya farklı okunurdu. Parmak izi yalnız BUILD
/// komut satırını anlatır.</para>
///
/// <para>Hash primitifi ve ayraç <see cref="BuildSignature"/>'ınkilerdir (kopya YASAK). Her öğe ayraç yanına
/// gömülmeden önce ayrıca özetlenir: öğe sınırı kayması (<c>-a</c>, <c>-bc</c> ↔ <c>-ab</c>, <c>-c</c>) ayırt
/// edilir.</para>
/// </summary>
public static class EngineFingerprint
{
    /// <summary>Proje yolu yerine istenen yer tutucu; listenin ilk öğesi olarak atılır.</summary>
    private const string ProjectPlaceholder = "<p>";

    /// <summary>Configuration yerine istenen yer tutucu: koşunun configuration değeri parmak izine girmez.</summary>
    private const string ConfigurationPlaceholder = "<c>";

    /// <summary>
    /// MSBuild.exe'nin dosya sürümünü okur ve saf hesaba verir. Okunamayan dosya (yok, erişim yok, geçersiz yol)
    /// sürümsüz sayılır, atmaz — testlerdeki sahte takımın yolu da böyle hesaplanır.
    /// </summary>
    public static string Compute(string msbuildExePath, Func<string, string, IReadOnlyList<string>> build)
    {
        ArgumentNullException.ThrowIfNull(msbuildExePath);
        return Compute(msbuildExePath, FileVersionOf(msbuildExePath), build);
    }

    /// <summary>
    /// Saf hesap: MSBuild.exe yolu + dosya sürümü (<c>null</c> ⇒ okunamadı, sabit işaretle girer) + build
    /// sözleşmesinin proje yolu dışındaki öğeleri → SHA-256 (büyük harf hex). I/O yok; aynı girdi her zaman aynı
    /// değeri üretir.
    /// </summary>
    public static string Compute(
        string msbuildExePath, string? fileVersion, Func<string, string, IReadOnlyList<string>> build)
    {
        ArgumentNullException.ThrowIfNull(msbuildExePath);
        ArgumentNullException.ThrowIfNull(build);

        var sb = new StringBuilder();
        AppendTerm(sb, msbuildExePath);
        AppendTerm(sb, fileVersion ?? BuildSignature.NullMarker);
        foreach (string argument in build(ProjectPlaceholder, ConfigurationPlaceholder).Skip(1))
            AppendTerm(sb, argument);
        return BuildSignature.HashText(sb.ToString());
    }

    private static void AppendTerm(StringBuilder sb, string term) =>
        sb.Append(BuildSignature.HashText(term)).Append(BuildSignature.ItemSeparator);

    private static string? FileVersionOf(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileVersion;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
