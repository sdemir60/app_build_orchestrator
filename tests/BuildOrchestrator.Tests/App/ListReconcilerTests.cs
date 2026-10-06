using System.Collections.ObjectModel;
using System.Collections.Specialized;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// <see cref="ListReconciler"/>'ın saf sözleşmesi: koleksiyon hedefe gelir, dokunulmayan öğeye dokunulmaz, bildirim
/// sayısı değişen öğe kadardır. (Bunun listedeki görünür sonucu — dokunulmayan satırın kontrolünün kalması —
/// <see cref="ListRowsStayRealizedTests"/>'te.)
/// </summary>
public class ListReconcilerTests
{
    private sealed record Header(string Name, int Rows, int Slot);
    private sealed class Row(string name) { public string Name { get; } = name; public override string ToString() => Name; }

    private static (ObservableCollection<object> current, List<NotifyCollectionChangedEventArgs> events) Collection(params object[] entries)
    {
        var current = new ObservableCollection<object>(entries);
        var events = new List<NotifyCollectionChangedEventArgs>();
        current.CollectionChanged += (_, e) => events.Add(e);
        return (current, events);
    }

    [Fact]
    public void The_same_sequence_changes_nothing()
    {
        var a = new Row("a"); var b = new Row("b");
        var (current, events) = Collection(new Header("L", 2, 0), a, b);

        bool changed = ListReconciler.Reconcile(current, [new Header("L", 2, 0), a, b]); // başlık AYNI değerde yeni bir kayıt

        Assert.False(changed);
        Assert.Empty(events);
    }

    [Fact]
    public void Removed_entries_leave_and_the_rest_are_untouched()
    {
        var a = new Row("a"); var b = new Row("b"); var c = new Row("c");
        var (current, events) = Collection(a, b, c);

        Assert.True(ListReconciler.Reconcile(current, [a, c]));

        Assert.Equal([a, c], current);
        Assert.Equal([NotifyCollectionChangedAction.Remove], events.Select(e => e.Action));
    }

    [Fact]
    public void Inserted_entries_enter_at_their_place_without_touching_neighbours()
    {
        var a = new Row("a"); var b = new Row("b"); var c = new Row("c");
        var (current, events) = Collection(a, c);

        Assert.True(ListReconciler.Reconcile(current, [a, b, c]));

        Assert.Equal([a, b, c], current);
        Assert.Equal([NotifyCollectionChangedAction.Add], events.Select(e => e.Action));
    }

    [Fact]
    public void A_moved_block_is_moved_not_rebuilt()
    {
        var rows = Enumerable.Range(0, 6).Select(i => new Row($"r{i}")).ToArray();
        var (current, events) = Collection(rows);

        // r3,r4 öne geçer: yalnız o ikisi taşınır, dört satır yerinde kalır.
        object[] target = [rows[0], rows[3], rows[4], rows[1], rows[2], rows[5]];
        Assert.True(ListReconciler.Reconcile(current, target));

        Assert.Equal(target, current);
        Assert.Equal([NotifyCollectionChangedAction.Move, NotifyCollectionChangedAction.Move], events.Select(e => e.Action));
    }

    [Fact]
    public void A_header_whose_count_changed_is_replaced_by_value()
    {
        var a = new Row("a"); var b = new Row("b");
        var (current, _) = Collection(new Header("L", 2, 0), a, b);

        Assert.True(ListReconciler.Reconcile(current, [new Header("L", 1, 0), a]));

        Assert.Equal([new Header("L", 1, 0), a], current);
    }

    [Fact]
    public void An_empty_target_clears_with_a_single_reset()
    {
        var (current, events) = Collection(new Row("a"), new Row("b"));

        Assert.True(ListReconciler.Reconcile(current, []));
        Assert.Empty(current);
        Assert.Equal([NotifyCollectionChangedAction.Reset], events.Select(e => e.Action));

        Assert.False(ListReconciler.Reconcile(current, [])); // boş → boş: dokunma
    }

    [Fact]
    public void Any_pair_of_sequences_ends_exactly_at_the_target()
    {
        var pool = Enumerable.Range(0, 12).Select(i => new Row($"r{i}")).ToArray();
        var random = new Random(20261007);
        for (int round = 0; round < 200; round++)
        {
            var from = pool.OrderBy(_ => random.Next()).Take(random.Next(0, pool.Length + 1)).Cast<object>().ToArray();
            var to = pool.OrderBy(_ => random.Next()).Take(random.Next(0, pool.Length + 1)).Cast<object>().ToArray();
            var current = new ObservableCollection<object>(from);

            bool changed = ListReconciler.Reconcile(current, to);

            Assert.Equal(to, current);
            Assert.Equal(!from.SequenceEqual(to), changed);
        }
    }
}
