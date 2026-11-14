using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BuildOrchestrator.Core.MsBuild;
using Xunit;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// [files-in-use · Task 4] Kilitli bir kopya hedefini yanındaki gizli <c>.in-use</c> klasörüne taşıyan, geri koyan ve süpüren
/// <see cref="InUseStash"/> — aracın çıktı klasörlerindeki TEK mutasyon yüzeyi. Görüntü olarak yüklenmiş bir DLL yeniden
/// adlandırılabilir ama silinemez; testte taşınabilir kilidi silmeyi paylaşan bir tutamaç (<c>FileShare.Read | FileShare.Delete</c>),
/// silinemeyen kilidi silmeyi paylaşmayan bir tutamaç (<c>FileShare.Read</c>) kurar. Süpürme yalnız kendi ad desenine uyan dosyaya
/// dokunur; <c>.pending</c> (sonucu bilinmeyen taşıma) boş yuvaya geri konur, <c>.old</c> asla geri konmaz.
/// </summary>
[Trait("Category", "MsBuild")]
public sealed class InUseStashTests
{
    private const string Id = "0123456789abcdef";

    private sealed class Setup : IDisposable
    {
        private readonly TempDir _temp = new();
        public string Folder { get; }
        public string Record { get; }
        public InUseStash Stash { get; }
        public string Parked => Path.Combine(Folder, InUseStash.FolderName);

        public Setup()
        {
            Folder = Path.Combine(_temp.Path, "Client", "bin");
            Directory.CreateDirectory(Folder);
            Record = Path.Combine(_temp.Path, "state", InUseStash.RecordFileName);
            Stash = new InUseStash(Record);
        }

        public string File(string name, string content = "old build")
        {
            string path = Path.Combine(Folder, name);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        /// <summary>Taşıma klasöründe elle bir kayıt: süpürmenin girdisi.</summary>
        public string ParkedEntry(string name, string state, string content = "parked")
        {
            Directory.CreateDirectory(Parked);
            string path = Path.Combine(Parked, $"{name}~{Id}.{state}");
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        public void RecordFolder() =>
            System.IO.File.WriteAllText(Directory.CreateDirectory(Path.GetDirectoryName(Record)!).FullName + "\\" + InUseStash.RecordFileName,
                JsonSerializer.Serialize(new[] { Folder }));

        public IReadOnlyList<string> RecordedFolders() =>
            System.IO.File.Exists(Record) ? JsonSerializer.Deserialize<List<string>>(System.IO.File.ReadAllText(Record))! : [];

        public void Dispose() => _temp.Dispose();
    }

    private static FileStream HoldLikeALoadedDll(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

    private static FileStream HoldWithoutDeleteSharing(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    [Fact]
    public void A_held_file_moves_into_a_hidden_folder_beside_it_and_the_folder_is_recorded()
    {
        using var s = new Setup();
        string path = s.File("Acme.Types.dll");
        StashedFile? moved;
        using (HoldLikeALoadedDll(path))
            moved = s.Stash.MoveAside(path);

        Assert.NotNull(moved);
        Assert.False(File.Exists(path));
        Assert.Equal(path, moved.OriginalPath);
        Assert.Matches(@"\\\.in-use\\Acme\.Types\.dll~[0-9a-f]{16}\.pending$", moved.StashPath);
        Assert.Equal("old build", File.ReadAllText(moved.StashPath));
        Assert.True(new DirectoryInfo(s.Parked).Attributes.HasFlag(FileAttributes.Hidden));
        Assert.Equal([s.Folder], s.RecordedFolders());
    }

    [Fact]
    public void A_file_held_without_delete_sharing_cannot_be_moved_and_stays_where_it_is()
    {
        using var s = new Setup();
        string path = s.File("Acme.Types.dll");

        using (HoldWithoutDeleteSharing(path))
            Assert.Null(s.Stash.MoveAside(path));

        Assert.Equal("old build", File.ReadAllText(path));
        Assert.False(Directory.Exists(s.Parked));
    }

    [Fact]
    public void Restoring_puts_the_file_back_into_an_empty_slot_and_leaves_a_filled_one_alone()
    {
        using var s = new Setup();
        string path = s.File("Acme.Types.dll");

        var first = s.Stash.MoveAside(path)!;
        Assert.True(s.Stash.Restore(first));
        Assert.Equal("old build", File.ReadAllText(path));
        Assert.Empty(Directory.Exists(s.Parked) ? Directory.GetFiles(s.Parked) : []);

        var second = s.Stash.MoveAside(path)!;
        File.WriteAllText(path, "new build"); // yeniden denemenin kopyası yuvayı doldurdu
        Assert.False(s.Stash.Restore(second));
        Assert.Equal("new build", File.ReadAllText(path));
        Assert.Matches(@"Acme\.Types\.dll~[0-9a-f]{16}\.old$", Assert.Single(Directory.GetFiles(s.Parked)));
    }

    [Fact]
    public void Settling_renames_the_parked_file_old()
    {
        using var s = new Setup();
        var moved = s.Stash.MoveAside(s.File("Acme.Types.dll"))!;

        s.Stash.Settle(moved);

        Assert.False(File.Exists(moved.StashPath));
        Assert.Matches(@"Acme\.Types\.dll~[0-9a-f]{16}\.old$", Assert.Single(Directory.GetFiles(s.Parked)));
    }

    [Fact]
    public void The_sweep_removes_what_nobody_holds_keeps_what_is_held_and_drops_the_folder_once_empty()
    {
        using var s = new Setup();
        s.File("a.dll", "new");
        s.File("b.dll", "new");
        s.ParkedEntry("a.dll", "old");
        string held = s.ParkedEntry("b.dll", "old");
        s.RecordFolder();

        StashSweep first;
        using (HoldWithoutDeleteSharing(held))
            first = s.Stash.Sweep();

        Assert.Equal(new StashSweep(Removed: 1, Restored: 0, Kept: 1), first);
        Assert.Equal([held], Directory.GetFiles(s.Parked));
        Assert.Equal([s.Folder], s.RecordedFolders());

        Assert.Equal(new StashSweep(Removed: 1, Restored: 0, Kept: 0), s.Stash.Sweep());
        Assert.False(Directory.Exists(s.Parked));
        Assert.Empty(s.RecordedFolders());
    }

    [Fact]
    public void The_sweep_restores_a_pending_file_into_an_empty_slot_and_never_an_old_one()
    {
        using var s = new Setup();
        s.ParkedEntry("x.dll", "pending", "x previous");
        s.ParkedEntry("y.dll", "old", "y previous");
        s.ParkedEntry("z.dll", "pending", "z previous");
        s.File("z.dll", "z new");
        s.RecordFolder();

        var sweep = s.Stash.Sweep();

        Assert.Equal(new StashSweep(Removed: 2, Restored: 1, Kept: 0), sweep);
        Assert.Equal("x previous", File.ReadAllText(Path.Combine(s.Folder, "x.dll")));
        Assert.False(File.Exists(Path.Combine(s.Folder, "y.dll")));
        Assert.Equal("z new", File.ReadAllText(Path.Combine(s.Folder, "z.dll")));
        Assert.False(Directory.Exists(s.Parked));
    }

    [Fact]
    public void The_sweep_leaves_a_file_it_did_not_park_alone_and_reads_a_broken_record_as_empty()
    {
        using var s = new Setup();
        Directory.CreateDirectory(s.Parked);
        string foreign = Path.Combine(s.Parked, "notes.txt");
        File.WriteAllText(foreign, "not ours");
        s.RecordFolder();

        Assert.Equal(new StashSweep(0, 0, 0), s.Stash.Sweep());
        Assert.True(File.Exists(foreign));

        File.WriteAllText(s.Record, "{ not json");
        Assert.Equal(new StashSweep(0, 0, 0), s.Stash.Sweep());
    }

    [Fact]
    public void Two_moves_at_once_into_one_folder_get_two_names_and_one_record_line()
    {
        using var s = new Setup();
        string a = s.File("a.dll");
        string b = s.File("b.dll");
        StashedFile? movedA = null, movedB = null;

        using (HoldLikeALoadedDll(a))
        using (HoldLikeALoadedDll(b))
            Parallel.Invoke(() => movedA = s.Stash.MoveAside(a), () => movedB = s.Stash.MoveAside(b));

        Assert.NotNull(movedA);
        Assert.NotNull(movedB);
        Assert.NotEqual(movedA.StashPath, movedB.StashPath);
        Assert.Equal(2, Directory.GetFiles(s.Parked).Length);
        Assert.Equal([s.Folder], s.RecordedFolders());
    }
}
