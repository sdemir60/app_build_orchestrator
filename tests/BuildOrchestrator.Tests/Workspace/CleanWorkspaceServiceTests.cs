using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Core.Workspace;
using Xunit;

namespace BuildOrchestrator.Tests.Workspace;

/// <summary>
/// [clean] <see cref="CleanWorkspaceService"/>: Clean butonunun motoru — keşfedilen her projenin
/// <c>bin</c>/<c>obj</c> klasörleri + o workspace'e ait build-state kayıtları silinir. Git GEREKMEZ (silme
/// düz dosya sistemi işidir), bu yüzden fixture ephemeral bir temp klasördür.
/// <para><b>Kapsam pinleri:</b> <c>packages\</c>, ortak OutDir ve proje klasörünün bin/obj DIŞINDAKİ alt
/// klasörleri AYNEN kalır; kilitli dosya hata DEĞİLDİR (atlanır + sayılır); silme sırası ÖNCE state SONRA
/// klasördür (ters sıra, imzası "güncel" görünen ama bin'i silinmiş bayat bir proje üretirdi).</para>
/// <para>[D8] Gerçek bekleme yok: retry gecikmesi <see cref="CleanWorkspaceService.DeleteRetryDelay"/>
/// dikişinden enjekte edilir (<c>BuildStateStore.RenameRetryDelay</c> deseni).</para>
/// </summary>
public class CleanWorkspaceServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bo-clean-" + Guid.NewGuid().ToString("N"));
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), "bo-clean-cache-" + Guid.NewGuid().ToString("N"));
    private readonly string _externalRoot = Path.Combine(Path.GetTempPath(), "bo-clean-ext-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        foreach (string dir in new[] { _root, _cacheRoot, _externalRoot })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* kilitli dosya testinden kalan handle — temizlik iddia taşımaz */ }
            catch (UnauthorizedAccessException) { }
        }
        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------- fixture

    private string Write(string relativePath, string content)
    {
        string full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    /// <summary>Bir proje klasörü kurar: csproj + bin/obj içinde birer çıktı dosyası.</summary>
    private string SeedProject(string name)
    {
        string dir = Path.Combine("src", name);
        Write(Path.Combine(dir, name + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        Write(Path.Combine(dir, name + ".cs"), "public class " + name + " { }");
        Write(Path.Combine(dir, "bin", "Debug", name + ".dll"), "binary-output");
        Write(Path.Combine(dir, "obj", "Debug", name + ".csproj.FileListAbsolute.txt"), "intermediate");
        return Path.Combine(_root, dir);
    }

    private CleanWorkspaceService NewService(BuildStateStore? store = null) =>
        new(new WorkspaceScanner(), store ?? new BuildStateStore(_cacheRoot)) { DeleteRetryDelay = _ => { } };

    private static List<IpcEvent> Run(CleanWorkspaceService service, string root)
    {
        var events = new List<IpcEvent>();
        service.Run(new CleanWorkspaceCommand(root), events.Add);
        return events;
    }

    /// <summary>[harici projeler] Ana kök DIŞINDA, kendi kökünde yaşayan bir proje — Settings'teki kartın
    /// karşılığı. Ana kökten ayrı bir ağaçtır, bu yüzden ana taramaya HİÇ takılmaz.</summary>
    private string SeedExternalProject(string name)
    {
        string dir = Path.Combine(_externalRoot, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        File.WriteAllText(Path.Combine(dir, name + ".cs"), "public class " + name + " { }");
        Directory.CreateDirectory(Path.Combine(dir, "bin"));
        File.WriteAllText(Path.Combine(dir, "bin", name + ".dll"), "binary-output");
        Directory.CreateDirectory(Path.Combine(dir, "obj"));
        File.WriteAllText(Path.Combine(dir, "obj", name + ".pdb"), "intermediate");
        return dir;
    }

    private static List<IpcEvent> RunWithExternals(CleanWorkspaceService service, string root, params string[] cards)
    {
        var events = new List<IpcEvent>();
        service.Run(
            new CleanWorkspaceCommand(root, [.. cards.Select(c => new ExternalProject(c))]),
            events.Add);
        return events;
    }

    private static BuildState State(string projectId) => new(projectId, "sig-" + Path.GetFileName(projectId));

    private static CleanCompletedEvent Completed(List<IpcEvent> events) =>
        Assert.IsType<CleanCompletedEvent>(events[^1]);

    private static IEnumerable<string> Lines(List<IpcEvent> events) =>
        events.OfType<CleanProgressEvent>().Select(e => e.Line);

    // ---------------------------------------------------------------- ana akış

    [Fact]
    public void Clean_removes_bin_and_obj_of_every_discovered_project_and_reports_the_summary()
    {
        string a = SeedProject("A");
        string b = SeedProject("B");

        var events = Run(NewService(), _root);

        Assert.False(Directory.Exists(Path.Combine(a, "bin")));
        Assert.False(Directory.Exists(Path.Combine(a, "obj")));
        Assert.False(Directory.Exists(Path.Combine(b, "bin")));
        Assert.False(Directory.Exists(Path.Combine(b, "obj")));
        Assert.True(File.Exists(Path.Combine(a, "A.csproj"))); // proje dosyasının kendisi DURUR

        var started = Assert.IsType<CleanStartedEvent>(events[0]);
        Assert.Equal(_root, started.RootPath);
        var done = Completed(events);
        Assert.Equal(2, done.ProjectCount);
        Assert.Equal(4, done.FoldersRemoved);
        Assert.True(done.BytesRemoved > 0, "silinen bayt sayısı raporlanmalı");
        Assert.Equal(0, done.LockedFileCount);
        Assert.NotEmpty(events.OfType<CleanProgressEvent>()); // started → progress* → completed
    }

    [Fact]
    public void Clean_does_not_touch_packages_outdir_or_anything_outside_project_folders()
    {
        string a = SeedProject("A");
        string package = Write(Path.Combine("packages", "Some.Lib.1.0.0", "lib", "Some.Lib.dll"), "restored");
        string sharedOutput = Write(Path.Combine("Output", "OSYS.Common.dll"), "shared out dir");
        string properties = Write(Path.Combine("src", "A", "Properties", "AssemblyInfo.cs"), "[assembly: X]");

        Run(NewService(), _root);

        Assert.False(Directory.Exists(Path.Combine(a, "bin")));
        Assert.True(File.Exists(package));
        Assert.True(File.Exists(sharedOutput));
        Assert.True(File.Exists(properties));
    }

    // [K-9] Sıra KRİTİKTİR: klasörler önce silinip state kalsaydı, imzası "güncel" görünen ama bin'i
    // silinmiş bir proje pre-skip edilir ve bayat çıktı verirdi. Bu test sırayı, state satırının yayınlandığı
    // ANDA diski gözleyerek pinler — kayıtlar o an gitmiş, klasörler ise HÂLÂ yerinde olmalı.
    [Fact]
    public void Clean_resets_the_build_state_before_deleting_folders()
    {
        string a = SeedProject("A");
        var store = new BuildStateStore(_cacheRoot);
        store.Upsert(new BuildState(Path.Combine(a, "A.csproj"), "sig-a"));
        store.Upsert(new BuildState(@"D:\other\C\C.csproj", "sig-c"));

        bool? stateGoneAtResetLine = null;
        bool? binStillThereAtResetLine = null;
        var service = NewService(store);
        service.Run(new CleanWorkspaceCommand(_root), ev =>
        {
            if (ev is CleanProgressEvent p && p.Line.Contains("build state reset", StringComparison.Ordinal))
            {
                stateGoneAtResetLine = !store.Load().ContainsKey(Path.Combine(a, "A.csproj"));
                binStillThereAtResetLine = Directory.Exists(Path.Combine(a, "bin"));
            }
        });

        Assert.True(stateGoneAtResetLine, "state satırı yayınlandığında kayıt zaten kalkmış olmalı");
        Assert.True(binStillThereAtResetLine, "state ÖNCE gider: o anda bin hâlâ durmalı");
        Assert.Equal([@"D:\other\C\C.csproj"], store.Load().Keys); // başka workspace'in state'i korunur
    }

    [Fact]
    public void Clean_reports_the_number_of_cleared_state_entries()
    {
        string a = SeedProject("A");
        var store = new BuildStateStore(_cacheRoot);
        store.Upsert(new BuildState(Path.Combine(a, "A.csproj"), "sig-a"));
        store.Upsert(new BuildState(@"D:\other\C\C.csproj", "sig-c"));

        Assert.Equal(1, Completed(Run(NewService(store), _root)).StateEntriesCleared);
    }

    // ---------------------------------------------------------------- hata modeli

    // [K-6] Kilitli dosya HATA DEĞİLDİR: o dosya atlanır, akış devam eder, sayaç + warn satırı raporlar.
    [Fact]
    public void A_locked_file_is_reported_and_skipped_without_stopping_the_clean()
    {
        string a = SeedProject("A");
        string b = SeedProject("B");
        string locked = Path.Combine(a, "bin", "Debug", "A.dll");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var events = Run(NewService(), _root);

            var done = Completed(events);
            Assert.True(done.LockedFileCount >= 1, "kilitli dosya sayılmalı");
            Assert.True(File.Exists(locked), "kilitli dosya silinemez");
            Assert.False(Directory.Exists(Path.Combine(b, "bin")), "akış öteki projeyle devam etmeli");
            Assert.False(Directory.Exists(Path.Combine(a, "obj")), "aynı projenin obj'i de temizlenmeli");
            Assert.Contains(events.OfType<CleanProgressEvent>(), e => e.Level == "warn");
        }
    }

    // [D8] Retry gecikmesi ENJEKTE EDİLEBİLİR olmalı: aksi halde bu testler gerçek zamanda beklerdi.
    [Fact]
    public void The_delete_retry_delay_is_injectable()
    {
        string a = SeedProject("A");
        int delays = 0;
        var service = new CleanWorkspaceService(new WorkspaceScanner(), new BuildStateStore(_cacheRoot))
        {
            DeleteRetryDelay = _ => delays++,
        };

        using (new FileStream(Path.Combine(a, "bin", "Debug", "A.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            service.Run(new CleanWorkspaceCommand(_root), _ => { });
        }

        Assert.True(delays > 0, "kilitli dosya retry edilmeli ve gecikme dikişten gelmeli");
    }

    // [K-6] Bozuk girdi IPC sınırında EXCEPTION'a değil TANIMLI bir hata event'ine dönüşür.
    [Fact]
    public void A_missing_workspace_root_becomes_a_defined_error_event()
    {
        var events = Run(NewService(), Path.Combine(_root, "does-not-exist"));

        var error = Assert.IsType<ErrorEvent>(events[^1]);
        Assert.Equal("cleanFailed", error.Code);
        Assert.DoesNotContain(events, e => e is CleanCompletedEvent);
    }

    // ---------------------------------------------------------------- harici kökler

    /// <summary>[harici projeler] Harici kökten gelen projeler SIRADAN projelerdir — aynı graf, aynı karar,
    /// aynı Clean. Kökleri ana repo DIŞINDA yaşadığı için tarama onları ancak kart listesiyle bulur; birleştirme
    /// Sync'in ve koşu planlayıcısının kullandığı <c>ExternalWorkspaceResolver</c> ile yapılır (kopya YASAK).
    /// Defter kayıtları da her kök için ayrı süpürülür: anahtar tam csproj yolu olduğundan ana kökün öneki
    /// harici kökü kapsamaz.</summary>
    [Fact]
    public void Clean_removes_bin_and_obj_of_external_projects_too()
    {
        string main = SeedProject("Main");
        string external = SeedExternalProject("Shared");
        var store = new BuildStateStore(_cacheRoot);
        store.Upsert(State(Path.Combine(main, "Main.csproj")));
        store.Upsert(State(Path.Combine(external, "Shared.csproj")));

        var done = Completed(RunWithExternals(NewService(store), _root, _externalRoot));

        Assert.False(Directory.Exists(Path.Combine(main, "bin")));
        Assert.False(Directory.Exists(Path.Combine(external, "bin")));
        Assert.False(Directory.Exists(Path.Combine(external, "obj")));
        Assert.Equal(2, done.ProjectCount);        // harici proje sıradan bir projedir, sayılır
        Assert.Equal(4, done.FoldersRemoved);
        Assert.Equal(2, done.StateEntriesCleared); // her kök ayrı süpürülür
        Assert.Empty(store.Load());
    }

    /// <summary>[güvenlik] Kart listesinde OLMAYAN bir kök HİÇ TARANMAZ, dolayısıyla çıktıları da durur. Silme
    /// izninin tek kaynağı bu çalışma alanının çözdüğü proje kümesidir; o kümeye girmenin tek yolu ana kökün
    /// altında olmak ya da kartla kaydedilmiş olmaktır.</summary>
    [Fact]
    public void A_root_that_was_not_registered_as_an_external_card_is_never_scanned()
    {
        SeedProject("Main");
        string stranger = SeedExternalProject("Stranger"); // kart olarak VERİLMEZ

        var done = Completed(Run(NewService(), _root));

        Assert.True(Directory.Exists(Path.Combine(stranger, "bin")));
        Assert.Equal(1, done.ProjectCount);
    }

    /// <summary>[harici projeler] Çözülemeyen kart Clean'i DURDURMAZ: Sync'in davranışının aynısı — uyarı
    /// satırı düşer, ana kök yine temizlenir.</summary>
    [Fact]
    public void An_unresolvable_external_card_warns_and_the_clean_carries_on()
    {
        string main = SeedProject("Main");
        string missing = Path.Combine(_externalRoot, "not-there");

        var events = RunWithExternals(NewService(), _root, missing);

        Assert.False(Directory.Exists(Path.Combine(main, "bin")));
        Assert.Contains(Lines(events), l => l.Contains("not-there", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, Completed(events).ProjectCount);
    }

    // ---------------------------------------------------------------- kenar durumlar

    [Fact]
    public void Two_csproj_in_the_same_folder_clean_that_folder_once()
    {
        Write(Path.Combine("src", "A", "A.csproj"), "<Project />");
        Write(Path.Combine("src", "A", "A.Tests.csproj"), "<Project />");
        Write(Path.Combine("src", "A", "bin", "A.dll"), "out");
        Write(Path.Combine("src", "A", "obj", "A.pdb"), "int");

        var done = Completed(Run(NewService(), _root));

        Assert.Equal(1, done.ProjectCount);
        Assert.Equal(2, done.FoldersRemoved);
    }

    [Fact]
    public void A_workspace_with_no_projects_completes_with_zero_counters()
    {
        Directory.CreateDirectory(_root);

        var done = Completed(Run(NewService(), _root));

        Assert.Equal(0, done.ProjectCount);
        Assert.Equal(0, done.FoldersRemoved);
        Assert.Equal(0, done.BytesRemoved);
    }

    [Fact]
    public void A_project_without_bin_or_obj_is_not_an_error()
    {
        Write(Path.Combine("src", "A", "A.csproj"), "<Project />");

        var done = Completed(Run(NewService(), _root));

        Assert.Equal(1, done.ProjectCount);
        Assert.Equal(0, done.FoldersRemoved);
    }

    // [güvenlik] bin içindeki bir junction/symlink İZLENMEZ: hedefin içeriği DURUR, yalnız bağlantı silinir.
    // Aksi halde bin'e konmuş bir bağlantı, silmeyi workspace'in tamamen dışına taşırdı.
    [SkippableFact]
    public void Clean_deletes_a_reparse_point_without_recursing_into_its_target()
    {
        string a = SeedProject("A");
        string target = Path.Combine(_cacheRoot, "link-target");
        Directory.CreateDirectory(target);
        string precious = Path.Combine(target, "precious.txt");
        File.WriteAllText(precious, "must survive");

        string link = Path.Combine(a, "bin", "linked");
        Skip.IfNot(TryCreateJunction(link, target), "bu ortamda junction oluşturulamıyor (mklink /J başarısız)");

        Run(NewService(), _root);

        Assert.False(Directory.Exists(Path.Combine(a, "bin")));
        Assert.True(File.Exists(precious), "junction hedefinin içeriğine DOKUNULMAMALI");
    }

    /// <summary>Junction (dizin bağlantısı) kurar. Symlink'in aksine yükseltilmiş hak gerektirmez ama yine de
    /// her ortamda çalışmaz — başarısızlık testi atlatır, gizlice yeşile boyamaz.</summary>
    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            process.WaitForExit(10_000);
            return process.HasExited && process.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
