using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [spec 2026-09-18 §6.3] Branch chip'i GERÇEK bir checkout yapar: popover'dan aktif olmayan bir branch'i seçmek
/// <see cref="CheckoutBranchCommand"/> gönderir; sonuç (<see cref="CheckoutCompletedEvent"/>) konsolu anlatır.
///
/// <para><b>Konsol kuralı (§6.2):</b> başarılı checkout yeni bir BÖLÜM açar — konsol ve olay akışı ÖNCE
/// temizlenir, stash ve switch satırları SONRA yazılır (yeni bölümün ilk satırları onlardır), ardından Sync
/// konsolu KORUYARAK zincirlenir. Reddedilen/başarısız checkout bölüm açmaz: konsol temizlenmez, uyarı altına
/// eklenir.</para>
///
/// <para>Harness <see cref="CleanCommandTests"/> ile aynıdır: başlatılmamış <see cref="EngineHost"/> — gönderim
/// SENKRON düşer ve VM içinde yutulur; uçuş penceresi gönderim ANINDA (<c>DebugOnCommandSent</c>) gözlenir,
/// motorun cevabı <c>vm.OnEvent(...)</c> ile verilir. D8: sleep/poll yok.</para>
/// </summary>
public class BranchCheckoutTests
{
    private const string FullSha = "b7e91d4a0c1f2e3d4c5b6a7980716253443526a1";
    private const string StashMessage = "build-orchestrator: leaving main for feature/x";

    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "aaaaaaaaaaaa", IsActive: true, IsRemoteTracking: false),
            new BranchRef("feature/x", "bbbbbbbbbbbb", IsActive: false, IsRemoteTracking: false),
            new BranchRef("origin/release", "cccccccccccc", IsActive: false, IsRemoteTracking: true),
        ]));
        return vm;
    }

    private static readonly BranchRef FeatureX = new("feature/x", "bbbbbbbbbbbb", false, false);

    private static string[] Lines(RunViewModel vm) =>
        vm.GetRunDocumentText().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // ---------------------------------------------------------------- gönderim

    /// <summary>Seçim hedefi, uzak mı olduğunu ve Settings → General'ın stash ayarını motora taşır. Uzak bir
    /// hedef <c>origin/x</c> biçiminde gider — izleyen yerel branch'i motor kurar.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Picking_another_branch_sends_a_checkout_with_the_stash_setting(bool stash)
    {
        var vm = NewVm();
        vm.StashOnBranchSwitch = stash;
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.SelectBranch(new BranchRef("origin/release", "cccccccccccc", false, IsRemoteTracking: true));

        var cmd = Assert.Single(sent.OfType<CheckoutBranchCommand>());
        Assert.Equal(new CheckoutBranchCommand(@"D:\repo", "origin/release", IsRemote: true, StashIfDirty: stash), cmd);
    }

    [Fact]
    public async Task Picking_the_active_branch_sends_nothing()
    {
        var vm = NewVm();
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        string before = vm.GetRunDocumentText();

        await vm.SelectBranch(new BranchRef("main", "aaaaaaaaaaaa", IsActive: true, IsRemoteTracking: false));

        Assert.Empty(sent);
        Assert.Equal(before, vm.GetRunDocumentText());
    }

    /// <summary>Gönderim anından motorun cevabına kadar chip kilitlidir (ikinci bir tık ikinci bir checkout
    /// kuyruklatırdı) ve kalıcı işlem pill'i işlemi adlandırır.</summary>
    [Fact]
    public async Task A_checkout_in_flight_locks_the_chip_and_names_the_operation()
    {
        var vm = NewVm();
        bool? lockedAtSend = null;
        string? opAtSend = null;
        vm.DebugOnCommandSent = c =>
        {
            if (c is not CheckoutBranchCommand) return;
            lockedAtSend = !vm.CanSwitchBranch;
            opAtSend = vm.CurrentOperation;
        };

        await vm.SelectBranch(FeatureX);

        Assert.True(lockedAtSend);
        Assert.Equal(OperationLabel.Checkout, opAtSend);
        Assert.True(vm.CanSwitchBranch); // gönderim düştü → hiçbir cevap gelmeyecek, kilit bırakılır
    }

    /// <summary>Gönderim SENKRON düştüyse hiçbir cevap gelmeyecek: pill "SWITCHING BRANCH"ta asılı kalmamalı.</summary>
    [Fact]
    public async Task A_checkout_that_could_not_be_sent_drops_the_operation_pill()
    {
        var vm = NewVm();

        await vm.SelectBranch(FeatureX);

        Assert.Null(vm.CurrentOperation);
    }

    // ---------------------------------------------------------------- uçuştaki checkout diğer işleri kilitler
    //
    // Supervisor checkout boyunca komut döngüsünü bloklar; o sırada basılan bir Build yeni ağaçta başlar ve
    // sonra checkout cevabının temizliği onun konsolunu siler, bir Pull ise yanlış branch'i ilerletir. Kapılar
    // gönderim ANINDA (istek penceresi) ölçülür — motor başlatılmadığı için gönderim hemen düşer.

    /// <summary>Uçuştaki checkout'un gönderim anındaki kapılarını ölçer. Topoloji ve "N behind" kurulur ki
    /// Build/Pull kapıları yalnız checkout yüzünden kapanmış olsun.</summary>
    private static async Task<Dictionary<string, bool>> GatesDuringCheckoutAsync()
    {
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent([new ProjectNode(@"C:\p\a.csproj", "A", @"C:\p\a.csproj", ["Osys"], [], 0, null, null, false, null)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0, Behind: 2));
        Assert.True(vm.BuildCommand.CanExecute(null) && vm.SyncCommand.CanExecute(null) && vm.PullRepositoryCommand.CanExecute(null));
        var gates = new Dictionary<string, bool>();
        vm.DebugOnCommandSent = c =>
        {
            if (c is not CheckoutBranchCommand) return;
            gates["build"] = vm.BuildCommand.CanExecute(null);
            gates["rebuild"] = vm.RebuildCommand.CanExecute(null);
            gates["row"] = vm.BuildProjectCommand.CanExecute(@"C:\p\a.csproj");
            gates["sync"] = vm.SyncCommand.CanExecute(null);
            gates["pull"] = vm.PullRepositoryCommand.CanExecute(null);
            gates["clean"] = vm.CleanCommand.CanExecute(null);
            gates["optimize"] = vm.OptimizeCommand.CanExecute(null);
        };
        await vm.SelectBranch(FeatureX);
        return gates;
    }

    [Fact]
    public async Task Build_is_locked_while_a_checkout_is_in_flight()
    {
        var gates = await GatesDuringCheckoutAsync();
        Assert.False(gates["build"]);
        Assert.False(gates["rebuild"]);
        Assert.False(gates["row"]);
    }

    [Fact]
    public async Task Sync_is_locked_while_a_checkout_is_in_flight()
        => Assert.False((await GatesDuringCheckoutAsync())["sync"]);

    [Fact]
    public async Task Pull_is_locked_while_a_checkout_is_in_flight()
        => Assert.False((await GatesDuringCheckoutAsync())["pull"]);

    [Fact]
    public async Task Clean_and_optimize_are_locked_while_a_checkout_is_in_flight()
    {
        var gates = await GatesDuringCheckoutAsync();
        Assert.False(gates["clean"]);
        Assert.False(gates["optimize"]);
    }

    // ---------------------------------------------------------------- sonuç → konsol

    /// <summary>Temizlik önce, not sonra: önceki işlemin satırı gider; yeni bölümün ilk iki satırı stash ve
    /// switch satırıdır (bu sırayla), ardından konsolu KORUYAN Sync gider.</summary>
    [Fact]
    public void A_successful_switch_clears_the_console_then_writes_the_stash_and_switch_lines()
    {
        var vm = NewVm();
        vm.OnEvent(new SyncProgressEvent("previous operation line", "info"));
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Switched, "main", "feature/x", FullSha, 2, StashMessage, null));

        var lines = Lines(vm);
        Assert.DoesNotContain("previous operation line", lines);
        Assert.Equal(PlanProgressLines.StashedBeforeSwitch(StashMessage), lines[0]);
        Assert.Equal(PlanProgressLines.SwitchedBranch("main", "feature/x", "b7e91d4"), lines[1]);
        // [spec 2026-09-18 §6.2] Branch değişiminin Sync'i ağa çıkmaz (SyncMode.BranchChange).
        Assert.False(Assert.Single(sent.OfType<SyncWorkspaceCommand>()).Fetch);

        // Zincirlenen Sync'in transkripti switch satırının ALTINA akar — ikinci bir temizlik yok.
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "feature/x"));
        vm.OnEvent(new SyncProgressEvent("git fetch origin feature/x", "cmd"));
        lines = Lines(vm);
        Assert.Equal(PlanProgressLines.StashedBeforeSwitch(StashMessage), lines[0]);
        Assert.Equal(PlanProgressLines.SwitchedBranch("main", "feature/x", "b7e91d4"), lines[1]);
        Assert.Equal("git fetch origin feature/x", lines[^1]);
        // Aradakiler yalnız bu harness'in başlatılmamış motorunun gönderim hataları (sync + listBranches);
        // gerçek motorda transkript doğrudan lines[2]'dir.
        Assert.All(lines[2..^1], l => Assert.StartsWith("[error] failed to send", l, StringComparison.Ordinal));
    }

    [Fact]
    public void A_refused_switch_keeps_the_console_and_appends_the_warning()
    {
        var vm = NewVm();
        vm.OnEvent(new SyncProgressEvent("previous operation line", "info"));
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));

        var lines = Lines(vm);
        Assert.Contains("previous operation line", lines);
        Assert.Equal(PlanProgressLines.SwitchRefusedDirty(3), lines[^1]);
        Assert.Empty(sent); // başarısız işlem bölüm açmaz, Sync zincirlemez
    }

    /// <summary>[Task 7] Konsolun açıklamalı uyarı satırının yanına (yukarıdaki test) event stream'e de KISA bir
    /// Warn satırı düşer — iki panel aynı reddi farklı ayrıntı seviyesinde anlatır. Metin
    /// <see cref="StreamText.BranchSwitchRefused"/>'ta (tek kaynak); rengi mevcut amber token'ı, glyph yok
    /// (sync/info'yla aynı ▸), daktiloyla gelir (Fail gibi anında DEĞİL).</summary>
    [Fact]
    public void A_refused_switch_also_writes_a_warn_line_to_the_stream()
    {
        // [fırtına dışı] nowMs enjekte edilir: iki push arasında >340ms olmazsa StreamComposer'ın kendi fırtına
        // kuralı (Instant=true) devreye girer ve Warn'ın "Fail gibi anında DEĞİL" kuralını ÖLÇÜLEMEZ kılar.
        long t = 0;
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1", () => t)
            { RootPath = @"D:\repo" };
        // İlk satır daktilo ETMEZ (prevNewest==null) — reddi ikinci satır yapıp ShouldType'ı ölçülebilir kılar.
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\a.csproj", SkipReasons.UpToDate));
        t += 1000;

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));

        var line = vm.StreamEvents[^1];
        Assert.Equal(StreamKind.Warn, line.Kind);
        Assert.Equal(StreamText.BranchSwitchRefused(3), line.Text);
        Assert.Equal("branch switch refused — 3 uncommitted files", line.Text);
        Assert.Equal("Brush.AmberText", line.TextBrushKey);
        Assert.Null(line.GlyphStatus);
        Assert.False(line.Instant);
        Assert.True(line.ShouldType);
    }

    /// <summary>[eksik negatif pin] Akışa yalnız KİRLİ AĞAÇ REDDİ düşer; checkout'un kendisi düştüyünde
    /// (<see cref="CheckoutStatus.Failed"/> / <see cref="CheckoutStatus.StashFailed"/>) akışa HİÇBİR satır
    /// yazılmaz — uyarı yalnız konsoldadır. Ayrım bilinçlidir: red kullanıcının yapabileceği bir şeydir
    /// (commit/stash), hata ise bir tanıdır ve run hikâyesine ait değildir. Yalnız <c>Dirty</c> dalı pinliydi;
    /// <c>PushStream</c> bu iki duruma da genişletilse süit sessiz kalırdı.</summary>
    [Theory]
    [InlineData(CheckoutStatus.Failed)]
    [InlineData(CheckoutStatus.StashFailed)]
    public void A_failed_checkout_writes_nothing_to_the_stream(CheckoutStatus status)
    {
        var vm = NewVm();
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\a.csproj", SkipReasons.UpToDate));
        int before = vm.StreamEvents.Count;
        Assert.True(before > 0); // ön-koşul: akış GERÇEKTEN yazıyor (vakumda yeşil kalmasın)

        vm.OnEvent(new CheckoutCompletedEvent(status, "main", "main", null, 0, null, "exit 1"));

        Assert.Equal(before, vm.StreamEvents.Count);
        Assert.Equal(PlanProgressLines.SwitchFailed("exit 1"), Lines(vm)[^1]); // konsol yine de söyler
    }

    /// <summary>Stash yapıldı ama checkout düştü: kullanıcının değişiklikleri stash'tedir — konsol bunu SÖYLEMEK
    /// zorundadır, yoksa değişiklikler kaybolmuş gibi görünür. Konsol yine temizlenmez.
    /// <para>[final review M2] Satırlar ardışıktır ama artık sonuncu olmak zorunda değildir: ardından gelen sessiz
    /// Sync'in (bkz. <see cref="A_failed_switch_after_a_stash_refreshes_silently"/>) bu harness'teki gönderim hatası
    /// altlarına düşer.</para></summary>
    [Fact]
    public void A_failed_switch_after_a_stash_still_says_where_the_changes_went()
    {
        var vm = NewVm();
        vm.OnEvent(new SyncProgressEvent("previous operation line", "info"));

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Failed, "main", "main", null, 2, StashMessage, "exit 1"));

        var lines = Lines(vm);
        Assert.Contains("previous operation line", lines);
        int stash = Array.IndexOf(lines, PlanProgressLines.StashedBeforeSwitch(StashMessage));
        Assert.True(stash >= 0, "stash satırı yok");
        Assert.Equal(PlanProgressLines.SwitchFailed("exit 1"), lines[stash + 1]);
    }

    /// <summary>[final review M2] Stash yapıldı ama checkout düştü: ağaç DEĞİŞTİ (değişiklikler stash'e gitti) — kararlar
    /// bayattır. Bölüm açılmaz; tek bir sessiz Sync (fetch'siz, konsol korunur, işlem pill'i yok) ekranı tazeler.</summary>
    [Fact]
    public void A_failed_switch_after_a_stash_refreshes_silently()
    {
        var vm = NewVm();
        vm.OnEvent(new SyncProgressEvent("previous operation line", "info"));
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Failed, "main", "main", null, 2, StashMessage, "exit 1"));

        Assert.False(Assert.Single(sent.OfType<SyncWorkspaceCommand>()).Fetch);
        Assert.Contains("previous operation line", Lines(vm));
        Assert.Null(vm.CurrentOperation);
        Assert.False(vm.CheckoutBusy);
    }

    /// <summary>Stash kendisi düştü: checkout hiç denenmedi, stash YOKTUR — stash satırı yazılmaz.</summary>
    [Fact]
    public void A_failed_stash_writes_only_the_failure()
    {
        var vm = NewVm();
        vm.OnEvent(new SyncProgressEvent("previous operation line", "info"));

        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.StashFailed, "main", "main", null, 2, StashMessage, "exit 1"));

        var lines = Lines(vm);
        Assert.Equal(["previous operation line", PlanProgressLines.SwitchFailed("exit 1")], lines.TakeLast(2));
        Assert.Empty(sent); // ağaç değişmedi — Sync yok
    }

    // ---------------------------------------------------------------- kilit

    [Fact]
    public async Task The_branch_chip_is_locked_mid_run()
    {
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent([new ProjectNode(@"C:\p\a.csproj", "A", @"C:\p\a.csproj", ["Osys"], [], 0, null, null, false, null)], [], [], []));
        Assert.True(vm.CanSwitchBranch);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.False(vm.CanSwitchBranch);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        await vm.SelectBranch(FeatureX);
        Assert.Empty(sent);

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        Assert.True(vm.CanSwitchBranch);
    }

    /// <summary>Uçuştaki bir Sync ya da motorun erişilemezliği de chip'i kilitler — Sync'in okuduğu ağaç altından
    /// değişmemeli, erişilemeyen motora gönderim anlamsızdır.</summary>
    [Fact]
    public void The_branch_chip_is_locked_while_a_sync_is_in_flight_or_the_engine_is_unavailable()
    {
        var vm = NewVm();
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.CanSwitchBranch);
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Assert.True(vm.CanSwitchBranch);

        vm.OnEngineUnavailable(@"D:\repo\supervisor\BuildOrchestrator.Supervisor.exe");
        Assert.False(vm.CanSwitchBranch);
    }

    /// <summary>Motorun reddi (koşu uçuşta) ya da beklenmeyen hatası: reddedilen checkout gibi davranılır —
    /// konsol temizlenmez, hata satırı altına eklenir, kilit açılır ve işlem pill'i düşer.</summary>
    [Theory]
    [InlineData("checkoutRejected")]
    [InlineData("checkoutFailed")]
    public async Task A_rejected_checkout_keeps_the_console_and_unlocks_the_chip(string code)
    {
        var vm = NewVm();
        vm.OnEvent(new SyncProgressEvent("previous operation line", "info"));
        bool? unlockedByError = null;
        vm.DebugOnCommandSent = c =>
        {
            if (c is not CheckoutBranchCommand) return;
            vm.OnEvent(new ErrorEvent(code, "A run is in flight — stop it before switching branches."));
            unlockedByError = vm.CanSwitchBranch;
        };

        await vm.SelectBranch(FeatureX);

        Assert.True(unlockedByError);
        Assert.Null(vm.CurrentOperation);
        Assert.Equal("previous operation line", Lines(vm)[0]);
        Assert.Contains(Lines(vm), l => l.Contains(code, StringComparison.Ordinal));
    }

    /// <summary>Motor checkout sırasında ölürse hiçbir cevap gelmez — kilit sızmamalı.</summary>
    [Fact]
    public async Task Losing_the_engine_mid_checkout_unlocks_the_chip()
    {
        var vm = NewVm();
        bool? unlockedByLoss = null;
        vm.DebugOnCommandSent = c =>
        {
            if (c is not CheckoutBranchCommand) return;
            vm.OnEngineExited(1);
            unlockedByLoss = vm.CanSwitchBranch;
        };

        await vm.SelectBranch(FeatureX);

        Assert.True(unlockedByLoss);
    }
}
