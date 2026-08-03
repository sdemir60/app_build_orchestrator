using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// Bir <b>envanterin</b> (branch listesi, worktree listesi) UI'a yayınlandığı koleksiyon: içerik TOPLUCA
/// değiştirilir ve bir yayın <b>en çok BİR</b> bildirim üretir — içerik gerçekten aynıysa <b>HİÇ</b>.
///
/// <para><b>Ölçülen kusur (bu tipin var oluş sebebi):</b> envanter eskiden öğe öğe uzlaştırılıyordu (sondan
/// N kez <c>RemoveAt</c>, sonra N kez <c>Add</c>) — yani yayın başına <b>2N bildirim</b>. Envanterin DÖRT
/// abonesi var (<c>BranchPopover</c>, <c>WorktreePopover</c>, <c>ActionBar</c>, başlık bağlamı) ve her biri
/// bildirim başına kendi görünümünü baştan kuruyor, yani O(n) iş yapıyor. Çarpım O(n²)'dir: gerçek OSYS
/// reposunun 475 branch'inde (<c>refs/heads</c> + <c>refs/remotes</c>) Sync başına <b>~18–36 saniye</b> UI
/// donması ölçüldü. Üstelik <c>SyncAsync</c> her Sync'te envanteri YENİDEN ister, dolayısıyla bu bedel
/// içerik hiç değişmemişken bile ödeniyordu.</para>
///
/// <para><b>Neden tek <c>Reset</c> bildirimi ([A13.2] koleksiyon-reset yasağıyla çelişmez):</b> o yasak
/// <see cref="RunViewModel.Projects"/> içindir ve iki somut şeyi korur — item container'ları ve satır
/// SEÇİMİ. Envanterlerde korunacak ikisi de YOKTUR: seçili branch bir <see cref="RunViewModel.Branch"/>
/// <b>değeridir</b> (koleksiyon öğesi değil, uzlaştırması <see cref="RunViewModel.ReconcileBranchWithInventory"/>'de)
/// ve aboneler zaten her bildirimde görünümlerini baştan kuruyor. Yani buradaki reset hiçbir durumu
/// düşürmez; yalnız N bildirimi 1'e indirir.</para>
///
/// <para><b>Değişmemiş yayın SESSİZDİR:</b> <see cref="ReplaceAll"/> içerik eşitse hiçbir şeye dokunmaz.
/// Öğeler <c>record</c> (değer eşitliği) olduğundan karşılaştırma sıra dahil birebirdir. Steady-state'te —
/// yani her normal Sync'te — envanter yolu tamamen bedava olur.</para>
/// </summary>
public sealed class SnapshotCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Koleksiyonun içeriğini <paramref name="snapshot"/> ile değiştirir.
    /// <list type="bullet">
    ///   <item>İçerik zaten aynıysa (sıra dahil) <b>hiçbir bildirim yayılmaz</b> ve <c>false</c> döner.</item>
    ///   <item>Farklıysa içerik <see cref="Collection{T}.Items"/> üzerinden sessizce kurulur ve TEK bir
    ///   <see cref="NotifyCollectionChangedAction.Reset"/> (+ <c>Count</c>/<c>Item[]</c>) yayılır; <c>true</c> döner.</item>
    /// </list>
    /// </summary>
    public bool ReplaceAll(IReadOnlyList<T> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (ContentEquals(snapshot)) return false;

        Items.Clear();
        for (int i = 0; i < snapshot.Count; i++) Items.Add(snapshot[i]);

        // WPF sözleşmesi: Reset'ten önce Count ve indeksleyici bildirimi (ObservableCollection'ın kendi deseni).
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        return true;
    }

    private bool ContentEquals(IReadOnlyList<T> snapshot)
    {
        if (snapshot.Count != Items.Count) return false;
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < snapshot.Count; i++)
            if (!comparer.Equals(Items[i], snapshot[i])) return false;
        return true;
    }
}
