using System.Collections.ObjectModel;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Gözlemlenebilir bir koleksiyonu hedef diziye EN AZ dokunuşla getirir: hedefte olmayan çıkar, hedefte olup
/// koleksiyonda olmayan girer, yeri değişen taşınır; eşit olan öğeye dokunulmaz. Eşitlik varsayılan karşılaştırıcıdır —
/// record'lar değerleriyle (katman başlığı), sınıflar referanslarıyla (satır modeli) eşleşir.
///
/// <para><b>Neden (ÖLÇÜLDÜ):</b> liste kaynağını takas etmek (<c>ItemsSource</c> ataması) WPF için tam reset'tir: her
/// container bırakılır ve pencere baştan kurulur — topoloji ve filtre tazelemesinde pencere dolusu satır yeniden
/// bağlanır ve ölçülürdü. Yerinde uzlaştırmada dokunulmayan satırın container'ı, ölçümü ve kaydırma konumu aynen
/// kalır; yalnız giren ve taşınan öğeler kurulur (<see cref="FixedHeightVirtualizingPanel"/>).</para>
/// </summary>
internal static class ListReconciler
{
    /// <summary>Koleksiyonu hedefe getirir. Döndürdüğü değer: koleksiyona dokunuldu mu.</summary>
    public static bool Reconcile(ObservableCollection<object> current, IReadOnlyList<object> target)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);

        if (target.Count == 0)
        {
            if (current.Count == 0) return false;
            current.Clear(); // tek bildirim (Reset) — tek tek silmek her öğe için ayrı bir tur demekti
            return true;
        }

        bool changed = false;

        // 1) Hedefte olmayanlar çıkar (sondan başa — indeksler kaymasın).
        var wanted = new HashSet<object>(target);
        for (int i = current.Count - 1; i >= 0; i--)
        {
            if (wanted.Contains(current[i])) continue;
            current.RemoveAt(i);
            changed = true;
        }

        // 2) Hedef sırasına hizala: yerindeyse geç, ileride duruyorsa taşı, yoksa ekle.
        for (int i = 0; i < target.Count; i++)
        {
            object entry = target[i];
            if (i < current.Count && Equals(current[i], entry)) continue;
            int at = IndexOfFrom(current, entry, i + 1);
            if (at >= 0) current.Move(at, i);
            else current.Insert(i, entry);
            changed = true;
        }
        return changed;
    }

    private static int IndexOfFrom(ObservableCollection<object> list, object entry, int start)
    {
        for (int i = start; i < list.Count; i++)
            if (Equals(list[i], entry)) return i;
        return -1;
    }
}
