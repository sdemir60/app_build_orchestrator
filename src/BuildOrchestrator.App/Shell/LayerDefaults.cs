using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// Settings'in VARSAYILAN katman tanımları — OSYS çözümünün sabit katman önekleri. İki tüketicisi vardır:
/// kayıtlı katman yokken Settings taslağının kurulumu ve "Restore default layers" butonu. Liste başka hiçbir
/// yerde tekrarlanmaz (tek doğruluk kaynağı).
///
/// <para>Proje adları <c>OSYS.&lt;Katman&gt;.&lt;Proje…&gt;</c> biçimindedir (ör.
/// <c>OSYS.Types.Service.WorkOrder</c>): önek sabittir, sonrası proje adıdır. Regex bu yapıya birebir uyar —
/// önek + nokta. Çıplak <c>OSYS.Types</c> adında bir proje BİLİNÇLİ olarak eşleşmez ve
/// <see cref="LayerEngine.OtherLayerName"/> katmanına düşer.</para>
///
/// <para>Eşleşme <see cref="ProjectNode.Name"/> (assembly kısa adı) üzerindedir ve
/// <see cref="LayerEngine.CompileUserPattern"/> pattern'leri <c>IgnoreCase</c> derler — <c>OSYS.UI</c> /
/// <c>OSYS.Ui</c> ayrımı sorun değildir.</para>
///
/// <para>Bu bir AÇILIŞ seed'i DEĞİLDİR: uygulama açılışında kalıcı duruma hiçbir şey yazılmaz, varsayılanlar
/// yalnız Settings taslağında görünür ve Save'e basılana dek ne motora ne diske gider.</para>
/// </summary>
public static class LayerDefaults
{
    /// <summary>Varsayılan katmanlar, KATMAN SIRASIYLA. Liste indeksi katman sırasıdır; Contracts'ın
    /// <see cref="LayerPattern.Order"/>'ı buradan DEĞİL, taslağın satır indeksinden türetilir
    /// (<c>SettingsDraftViewModel.BuildPatterns</c>) — sıra tek yerde yorumlanır.</summary>
    public static readonly IReadOnlyList<(string Name, string Regex)> Layers =
    [
        ("OSYS.Types", @"^OSYS\.Types\."),
        ("OSYS.Business", @"^OSYS\.Business\."),
        ("OSYS.Orchestration", @"^OSYS\.Orchestration\."),
        ("OSYS.UI", @"^OSYS\.UI\."),
    ];
}
