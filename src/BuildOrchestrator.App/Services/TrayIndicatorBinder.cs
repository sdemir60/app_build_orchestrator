using System.ComponentModel;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [tray indicator/K-12] VM sinyallerini tepsi göstergesinin controller'ına bağlayan tek yer.
///
/// <para><b>Neden ayrı bir tip:</b> bu kablaj küçük ama SESSİZCE yanlış olabilir — hangi property'nin hangi
/// girdiyi beslediği ve bitiş metninin şeridin o anki satırı olduğu buradaki birkaç satırda yaşar. Kablaj
/// <c>MainWindow.OnSourceInitialized</c>'ın içinde kalsaydı hiçbir test
/// göremezdi: o metot gerçek bir tepsi ikonu kurup global kısayol kaydeder ve süit onu bilerek hiç
/// çalıştırmaz. Buraya alınınca pencere de HWND de gerekmez.</para>
///
/// <para><b>Metin neden her sinyalde itiliyor:</b> controller bildirimi çıkış evresinin SONUNA erteler
/// (saniyeler sonra), ve bu arada VM değerlerini tamamlar — koşu bittiğinde önce <c>Phase</c>, hemen ardından
/// <c>Counters</c> yayınlanır. Metin faz anında BİR KEZ yakalansaydı balloon yarım bir cümle taşırdı
/// ("Completed — 0 succeeded"). Push modeli, controller'ın bildirimi verdiği anda elindeki satırın en güncel
/// satır olmasını sağlar.</para>
///
/// <para><b>Neden property adı SEÇİLMİYOR:</b> ilk hâli şeridin dinlediği on kadar property'yi tek tek
/// sayıyordu. İki kusuru vardı. Biri ÖLÇÜLDÜ: <c>Counters</c> dalı çıkarıldığında hiçbir test kırılmadı —
/// yani sayılan dalların bir kısmı kanıtsızdı (satırı besleyen sinyaller birbirini zaten tetikliyor). Diğeri
/// daha kötüydü: liste şeridin listesinin KOPYASIYDI ve şerit yarın yeni bir girdi kazandığında burası
/// sessizce eski kalırdı — tam olarak bu tipin önlemek için var olduğu ayrışma. Satır zaten bu
/// property'lerin BİLEŞİMİDİR; hangisi değişirse değişsin yeniden okumak hem doğru hem de listeyi
/// gereksiz kılar.</para>
///
/// <para><b>Maliyet:</b> bir <c>RibbonText.Compose</c> çağrısı — saf string biçimleme. En yoğun sinyal koşarken
/// tick eden <c>ElapsedMs</c>'tir (saniyede birkaç kez) ve ekrandaki şerit ZATEN her tick'te aynı çağrıyı
/// yapıp bir <c>TextBlock</c> güncelliyor; buradaki iş ondan azdır.</para>
/// </summary>
internal static class TrayIndicatorBinder
{
    /// <summary>VM'i controller'a bağlar ve o anki durumu bir kez iter. Abonelik idempotenttir
    /// (<c>-=</c> sonra <c>+=</c>): iki kez bağlanmak çift itme üretmez.</summary>
    public static void Attach(RunViewModel vm, TrayBuildIndicatorController controller)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(controller);

        void OnChanged(object? sender, PropertyChangedEventArgs e) => Route(vm, controller, e.PropertyName);

        vm.PropertyChanged -= OnChanged;
        vm.PropertyChanged += OnChanged;

        PushLine(vm, controller);
        controller.SetPhase(vm.Phase);
    }

    private static void Route(RunViewModel vm, TrayBuildIndicatorController controller, string? property)
    {
        // Satır her sinyalde tazelenir — ve fazdan ÖNCE: faz aktif kümeden çıkarsa controller bitişi tam o
        // anda kurar ve elinde bir metin bulmalıdır.
        PushLine(vm, controller);

        if (property is nameof(RunViewModel.Phase))
            controller.SetPhase(vm.Phase);
    }

    /// <summary>[K-5] Bildirime giden şey şeridin o anki SATIRIDIR — parçalanıp yeniden kurulmaz. Metin,
    /// sağlık ve bildirimin başlık/gövde ayrımı hepsi o satırın içindedir.</summary>
    private static void PushLine(RunViewModel vm, TrayBuildIndicatorController controller) =>
        controller.SetTerminalLine(vm.RibbonLine);
}
