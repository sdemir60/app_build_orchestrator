using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 2 son inceleme O4] Bir testin açtığı anlatı spool'unu klasörü silinmeden ÖNCE kapatan kapsam — "spool klasörden
/// önce kapanır" kuralının TEK yeri: dispose store'u temizler (<see cref="NarrativeStore.Clear"/> spool'u kapatır; dosya diskte kalır).
/// Açık bir yazıcı klasörün silinmesini engeller ve klasörü silen taraf (<see cref="TempDir"/>, gerçek motorun sandbox'ı) o hatayı yutar:
/// tutamaç da klasör de sessizce sızardı. Klasörü kendisi kuran testler <see cref="SpoolDir"/>'i kullanır (o da bu kapsamı taşır); klasörü
/// başkasının olan testler (gerçek motorun sandbox'taki koşu klasörü, VM'den önce kurulan bir klasör) bu kapsamı klasörden SONRA tanımlar —
/// <c>using</c>'ler ters sırada kapanır, test nasıl biterse bitsin (düşen bir iddia, zaman aşımı, Skip). Store'u yeni bir işlem çoktan
/// temizlediyse ikinci temizlik zararsızdır.
/// </summary>
internal sealed class SpoolScope(NarrativeStore store) : IDisposable
{
    public void Dispose() => store.Clear();
}
