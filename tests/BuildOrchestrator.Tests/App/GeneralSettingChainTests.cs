using System.IO;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.App.ViewModels.GeneralSettingsCatalog;
using static BuildOrchestrator.Tests.App.GeneralSettingChains;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [B3 · karar 47a] Settings → General'ın HER boolean ayarının zinciri — tek teori sınıfı, satırlar <see cref="GeneralSettingChains"/>
/// tablosundan: katalog → taslak → ayar dosyası → Save (UiState + VM aynı commit'te) → kabuğun açılış tohumu ve kalıcılığı; kabuk
/// anahtarlarında <see cref="ShellSwitches"/> okuyucusu, ui-state dosyası ve Save'in notları. Ayara özgü davranış kendi sınıfındadır:
/// harici projesiz kurulumda pull'un sessizliği (<see cref="PullBeforeBuildTests"/>), Resolve anahtarının koşu komutuna gitmesi
/// (<see cref="ResolveAtFullPrioritySettingsTests"/>), boşta kuralının metinleri ve fixture sözleşmesi
/// (<see cref="StopEngineWhenIdleSettingsTests"/>), Windows başlangıç kaydı (<see cref="StartWithWindowsTests"/>), checkout'un stash'i
/// (<see cref="BranchCheckoutTests"/>), kompakt kipin kendisi (<see cref="CompactConsoleTests"/>).
/// <para><b>[DEĞİŞEN DÜZEN — B3]</b> Eskiden her ayarın kendi zincir sınıfı vardı ve her yeni ayar bir öncekinin kopyasıydı (Pull →
/// Stash → Resolve → Compact → Stop engine; kabuk anahtarlarında <c>ShellSwitchesTests</c> ile <c>StartWithWindowsTests</c>'in ikiz
/// testleri). Kopyalar ayrışmıştı: Resolve'un satır/bayrak testi, Pull'un kabuk tohumu ve uygulamanın ayarı taşımadığı durum yalnız
/// bazı ayarlarda vardı. Eski test → satır eşlemesi B3 raporundadır (<c>w3-b3-report.md</c>); hiçbir iddia düşmedi.</para>
/// <para>Kabuk anahtarlarının davranışı bu sınıfta BAĞLANMAZ — pencere kapanışını <c>MainWindow.OnClosing</c>, tepsi balonlarını
/// <c>FirstCloseBalloonGate</c>/<c>TrayBuildIndicatorController</c>/<c>SecondInstanceGate</c>/<c>MainWindow.OnGlobalHotkey</c>, açılış yolunu
/// <c>StartupArgs.Decide</c> okur (<see cref="ShellSwitches"/> üzerinden; testleri <see cref="CloseToTrayTests"/> ve tepsi testleri).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class GeneralSettingChainTests
{
    /// <summary>Kataloğun her satırı — tablodan değil KATALOGDAN: tabloya girmemiş yeni bir ayar teoride adıyla düşer.</summary>
    public static TheoryData<GeneralSetting> EverySetting() => DataOf(GeneralSettingsCatalog.Groups.SelectMany(g => g.Rows).Select(r => r.Setting));

    /// <summary>VM'de yaşayan beş iş akışı tercihi.</summary>
    public static TheoryData<GeneralSetting> WorkflowSettings() => DataOf(Workflow.Select(c => c.Setting));

    /// <summary>Dört kabuk anahtarı.</summary>
    public static TheoryData<GeneralSetting> ShellSettings() => DataOf(Shell.Select(c => c.Setting));

    /// <summary>Her iş akışı tercihi, diyalog iki değerin her biriyle açılarak.</summary>
    public static TheoryData<GeneralSetting, bool> WorkflowSettingsBothWays()
    {
        var data = new TheoryData<GeneralSetting, bool>();
        foreach (var chain in Workflow)
        {
            data.Add(chain.Setting, true);
            data.Add(chain.Setting, false);
        }
        return data;
    }

    private static TheoryData<GeneralSetting> DataOf(IEnumerable<GeneralSetting> settings)
    {
        var data = new TheoryData<GeneralSetting>();
        foreach (var setting in settings) data.Add(setting);
        return data;
    }

    private static string Json(bool value) => value ? "true" : "false";

    /// <summary>Motoru hiç başlatılmayan, kökü <see cref="GeneralSettingChains.Root"/> olan bir koşu VM'i.</summary>
    private static RunViewModel NewRun(EngineHost engine) => new(engine, NeverTickingBatcher(), () => "r1") { RootPath = Root };

    // ---------------------------------------------------------------- tablo

    /// <summary>Tablo kataloğu eksiksiz kapsar ve satır türleri üretimin ayrımıyla aynıdır: kabuk satırları <see cref="ShellSwitches.All"/>'ın
    /// ayarlarıdır, geri kalan her satır bir iş akışı tercihidir.</summary>
    [Fact]
    public void The_table_has_one_row_per_catalog_row_and_the_shell_rows_are_the_shell_switches()
    {
        Assert.Equal(GeneralSettingsCatalog.Groups.SelectMany(g => g.Rows).Select(r => r.Setting), All.Select(c => c.Setting));
        Assert.Equal(ShellSwitches.All.Select(s => s.Setting), Shell.Select(c => c.Setting));
        Assert.All(All, c => Assert.True((c.Workflow is null) != (c.Shell is null), $"{c.Setting}: a row is either a workflow setting or a shell switch"));
    }

    // ---------------------------------------------------------------- varsayılan

    /// <summary>
    /// [B3 · karar 47a — karakterizasyon pini] Varsayılanın TEK tanımı kataloğun satırıdır (<see cref="GeneralSettingDefinition.Default"/>):
    /// ayarı okuyan her yer taze hâlinde onu gösterir — taze taslağın satırı (ve iş akışı tercihinde taslağın bayrağı), taze bir VM (Compact
    /// console'da etkin kip de), anahtarı taşımayan bir ui-state'ten açılan kabuğun KENDİ tohumu; kabuk anahtarında <see cref="ShellSwitches.IsOn"/>
    /// ve anahtarın kendi okuyucusu. Kendi literalini taşıyan bir yer kataloğun değeri değişince ondan ayrışır ve satırı kırmızı verir —
    /// yer adıyla raporlanır.
    /// <para>Eski iki karakterizasyon testi (<c>CompactConsoleSettingsTests</c> ve <c>StopEngineWhenIdleSettingsTests</c>'in
    /// <c>A_fresh_vm_a_fresh_draft_and_a_key_less_seed_show_the_catalogs_default</c>) yalnız tek-kaynaklı iki ayarı kapsıyordu; bu pin
    /// dokuz ayarın hepsini kapsar.</para>
    /// <para>KIRMIZI (geçici bozma, karar 47a'nın refaktöründen önce: katalogda Pull before build, Stash and switch branches ve Resolve cycles
    /// at full priority çevrildi): üç satır da dört yerde ayrıştı — taslağın satırı ve bayrağı (ctor'un literal argümanı), taze VM (alanın
    /// literali), kabuğun tohumu (<c>?? literal</c>); ör. <c>ResolveAtFullPriority — a fresh view model: True, the catalog's default: False</c>.
    /// Refaktörden sonra aynı çevirme bu pinde hiçbir satırı kırmızıya döndürmez; yalnız onaylanmış değerin pini
    /// (<see cref="Every_setting_has_its_approved_default"/>) kırmızı verir.</para>
    /// </summary>
    [StaTheory]
    [MemberData(nameof(EverySetting))]
    public void Every_reader_of_a_setting_starts_on_the_catalogs_default(GeneralSetting setting)
    {
        var chain = Of(setting);
        bool catalog = DefaultOf(setting);
        var draft = new SettingsDraftViewModel(null, Root);
        var sites = new List<(string Site, bool Value)> { ("a fresh draft's row", draft.GeneralRow(setting).IsOn) };
        if (chain.Workflow is { } workflow)
        {
            sites.Add(("a fresh draft's flag", workflow.Draft(draft)));
            var fresh = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1");
            sites.Add(("a fresh view model", workflow.Vm(fresh)));
            if (workflow.InEffect is { } inEffect) sites.Add(("a fresh view model's mode in effect", inEffect(fresh)));

            using var temp = new TempDir();
            var (window, seeded) = New(temp, saved: new UiState(), compactConsole: null, stopEngineWhenIdle: null); // pencerenin KENDİ tohumu
            sites.Add(("the window's seed from a key-less state", workflow.Vm(seeded)));
            if (workflow.InEffect is { } seededInEffect) sites.Add(("the window's seed, mode in effect", seededInEffect(seeded)));
            GC.KeepAlive(window);
        }
        if (chain.Shell is { } shell)
        {
            sites.Add(("ShellSwitches.IsOn over a key-less state", ShellSwitches.IsOn(new UiState(), setting)));
            sites.Add(("the switch's own reader over a key-less state", shell.Reads(new UiState())));
        }

        Assert.All(sites, site => Assert.True(site.Value == catalog, $"{setting} — {site.Site}: {site.Value}, the catalog's default: {catalog}"));
    }

    /// <summary>Kataloğun satırı onaylanmış varsayılanı taşır (spec/kullanıcı kararları — tablonun <see cref="GeneralSettingChain.ApprovedDefault"/>'u
    /// tek literal pinidir) ve etiket katalogda TEK satırdadır. Yukarıdaki pinle birlikte: ayarı okuyan her yer onaylanmış değerle başlar.</summary>
    [Theory]
    [MemberData(nameof(EverySetting))]
    public void Every_setting_has_its_approved_default(GeneralSetting setting)
    {
        var chain = Of(setting);
        var row = SettingsDialogHost.CatalogRow(chain.Label);

        Assert.Equal(setting, row.Setting);
        Assert.Equal(chain.ApprovedDefault, row.Default);
    }

    // ---------------------------------------------------------------- taslak

    /// <summary>İş akışı tercihinin satırı ve taslağın bayrağı TEK değerdir: satır değişince taslağın özelliği de bildirir, özellik yazılınca
    /// satır değişir.</summary>
    [Theory]
    [MemberData(nameof(WorkflowSettings))]
    public void A_workflow_settings_row_and_its_draft_flag_are_one_value(GeneralSetting setting)
    {
        var workflow = Of(setting).Workflow!;
        bool other = !DefaultOf(setting);
        var draft = new SettingsDraftViewModel(null, Root);
        var notified = new List<string?>();
        draft.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        draft.GeneralRow(setting).IsOn = other;
        Assert.Equal(other, workflow.Draft(draft));
        Assert.Contains(workflow.DraftFlag, notified);

        workflow.SetDraft(draft, !other);
        Assert.Equal(!other, draft.GeneralRow(setting).IsOn);
    }

    /// <summary>Diyalog taslağı canlı değerden kurar (Cancel onu atabilsin diye kopyadır): ctor'a verilen değer satırın ve bayrağın değeridir.</summary>
    [Theory]
    [MemberData(nameof(WorkflowSettings))]
    public void The_draft_opens_on_the_live_value_it_is_given(GeneralSetting setting)
    {
        var workflow = Of(setting).Workflow!;
        bool other = !DefaultOf(setting);

        var draft = workflow.NewDraft(other);

        Assert.Equal(other, workflow.Draft(draft));
        Assert.Equal(other, draft.GeneralRow(setting).IsOn);
    }

    /// <summary>Diyalog açılışının ctor'u: kayıtlı durum verildiyse kabuk anahtarının satırı kayıtlı değerden tohumlanır (Windows yüzeyi yok).</summary>
    [Theory]
    [MemberData(nameof(ShellSettings))]
    public void The_draft_opens_a_shell_switch_on_its_saved_value(GeneralSetting setting)
    {
        var chain = Of(setting);
        bool other = !DefaultOf(setting);
        var saved = new UiState();
        chain.SetState(saved, other);

        var draft = new SettingsDraftViewModel(null, Root, saved: saved);

        Assert.Equal(other, draft.GeneralRow(setting).IsOn);
    }

    // ---------------------------------------------------------------- ayar dosyası

    /// <summary>
    /// Export anahtarı yazar (tipli alan ve JSON), Import geri okur; diğer her ayar varsayılanında kalır. Varsayılan DIŞI değer kullanılır:
    /// bağlantı koparsa form varsayılanda kalır ve satır kırmızı verir.
    /// <para><b>[DEĞİŞEN KURAL — P3 2026-09-28 · P4 2026-09-29, kullanıcı kararları]</b> Kabuk anahtarları için eski iddia
    /// (<c>The_four_new_switches_are_not_written_to_the_settings_file</c>): dördü ayar dosyasına girmezdi — çevrilseler de export JSON'u
    /// varsayılanla aynıydı. Dördü de kalıcıdır ve dosyada taşınır.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EverySetting))]
    public void Every_setting_round_trips_through_the_settings_file(GeneralSetting setting)
    {
        var chain = Of(setting);
        bool other = !DefaultOf(setting);
        var draft = new SettingsDraftViewModel(null, Root);
        draft.GeneralRow(setting).IsOn = other;

        var file = draft.ToFile();
        Assert.Equal(other, chain.File(file));
        string json = file.ToJson();
        Assert.Contains($"\"{chain.FileKey}\": {Json(other)}", json, StringComparison.Ordinal);

        var loaded = new SettingsDraftViewModel(null, Root);
        loaded.LoadFrom(SettingsFile.TryParse(json)!);

        Assert.Equal(other, loaded.GeneralRow(setting).IsOn);
        if (chain.Workflow is { } workflow) Assert.Equal(other, workflow.Draft(loaded));
        Assert.All(All.Where(c => c.Setting != setting),
            c => Assert.True(loaded.GeneralRow(c.Setting).IsOn == DefaultOf(c.Setting), $"{c.Setting} left its default"));
    }

    /// <summary>Anahtarı taşımayan (eski ya da yalnız katman) bir dosya formdaki değeri varsayılana döndürmez — bir dosyanın taşımadığı
    /// ayarı sıfırlamak sessiz bir karar olurdu (harici liste kuralının aynısı). Olumsuz durumun pinidir; okumanın gerçekten yapıldığını
    /// round-trip teorisi kanıtlar.</summary>
    [Theory]
    [MemberData(nameof(EverySetting))]
    public void A_file_without_the_setting_leaves_the_form_untouched(GeneralSetting setting)
    {
        bool other = !DefaultOf(setting);
        var draft = new SettingsDraftViewModel(null, Root);
        draft.GeneralRow(setting).IsOn = other;

        draft.LoadFrom(SettingsFile.From(Root, []));

        Assert.Equal(other, draft.GeneralRow(setting).IsOn);
    }

    // ---------------------------------------------------------------- Save → VM (iş akışı tercihleri)

    /// <summary>Save iş akışı tercihini AYNI commit'te kalıcı duruma yazar ve VM'e uygular.</summary>
    [Theory]
    [MemberData(nameof(WorkflowSettings))]
    public async Task Save_persists_a_workflow_setting_and_applies_it_in_the_same_commit(GeneralSetting setting)
    {
        var chain = Of(setting);
        var workflow = chain.Workflow!;
        bool other = !DefaultOf(setting);
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = NewRun(engine);
        var store = new SettingsDialogHost.FakeStore();
        var draft = new SettingsDraftViewModel(null, Root);
        workflow.SetDraft(draft, other);

        await draft.CommitAsync(run, store);

        Assert.Equal(other, workflow.Vm(run));
        Assert.Equal<bool?>(other, chain.State(store.State));
    }

    /// <summary>Değişen ayar VM'e uygulanır ve konsola BİR KEZ not düşer; değişmeyen ayar (aynı değerle ikinci Save dahil) sessizdir.
    /// Pull before build'in uygulaması bir harici proje kartıyla gelir: notu yalnız harici proje varken yazılır (kartsız sessizlik
    /// <see cref="PullBeforeBuildTests"/>'tedir).</summary>
    [Theory]
    [MemberData(nameof(WorkflowSettings))]
    public async Task A_changed_workflow_setting_notes_it_once_and_an_unchanged_one_stays_quiet(GeneralSetting setting)
    {
        var chain = Of(setting);
        var workflow = chain.Workflow!;
        bool initial = DefaultOf(setting);
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = NewRun(engine);

        await workflow.Apply(run, initial);
        Assert.DoesNotContain(chain.Label, run.GetRunDocumentText(), StringComparison.Ordinal);

        await workflow.Apply(run, !initial);
        Assert.Equal(!initial, workflow.Vm(run));
        Assert.Equal(1, Occurrences(run.GetRunDocumentText(), workflow.Note(!initial)));

        await workflow.Apply(run, !initial); // aynı değerle ikinci Save: değişmeyen ayar sessizdir
        Assert.Equal(1, Occurrences(run.GetRunDocumentText(), workflow.Note(!initial)));

        await workflow.Apply(run, initial);
        Assert.Equal(initial, workflow.Vm(run));
        Assert.Equal(1, Occurrences(run.GetRunDocumentText(), workflow.Note(initial)));
    }

    /// <summary>
    /// [B3 · karar 47a] Ayarı TAŞIMAYAN bir uygulama onu değiştirmez ve not yazmaz: <see cref="RunViewModel.ApplySettingsAsync"/>'in beş
    /// General parametresi de <c>bool?</c>'dur ve verilmemesi "olduğu gibi kalsın" demektir. Settings'in Save'i her değeri her zaman verir
    /// (<see cref="SettingsDraftViewModel.CommitAsync"/>); ayarı hiç anmayan bir çağıran — başka bir ayarı sınayan test — onu kendiliğinden
    /// değiştirip konsola not düşmemeli.
    /// <para><b>[DEĞİŞEN KURAL — karar 47a]</b> Eski iddia (yalnız Compact console ve Stop the engine when idle için — dalga 2 karar 16 ve
    /// dalga 3): bu iki parametre <c>bool?</c>'du, Pull before build / Stash and switch branches / Resolve cycles at full priority ise
    /// <c>bool</c> ve literal varsayılanlıydı — onları anmayan bir çağrı üçünü literal değere SIFIRLIYORDU. Değişme gerekçesi: varsayılanın
    /// tek tanımı kataloğun satırıdır (literal bir argüman varsayılanı ikinci bir tanımdı) ve beş parametre aynı kuralı izler. Üretimin
    /// tek çağıranı her değeri verdiği için kullanıcının gördüğü davranış değişmez.</para>
    /// <para>KIRMIZI (refaktörden önce): üç satır — anmayan çağrı ayarı literale sıfırladı: Pull before build <c>Expected: False, Actual:
    /// True</c>, Stash and switch branches <c>Expected: True, Actual: False</c>, Resolve cycles at full priority <c>Expected: False, Actual:
    /// True</c>.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(WorkflowSettings))]
    public async Task An_apply_that_does_not_carry_a_workflow_setting_leaves_it_unchanged_and_quiet(GeneralSetting setting)
    {
        var chain = Of(setting);
        var workflow = chain.Workflow!;
        bool other = !DefaultOf(setting);
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = NewRun(engine);
        workflow.SetVm(run, other);

        await run.ApplySettingsAsync([], Root, []);

        Assert.Equal(other, workflow.Vm(run));
        Assert.DoesNotContain(chain.Label, run.GetRunDocumentText(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- kabuk: seed + kalıcılık (iş akışı tercihleri)

    /// <summary>Açılışta kalıcı durumdan tohumlanır; VM'de değişince kalıcı duruma yazılır. Fixture'ın iki ataması kapatılır
    /// (<c>compactConsole: null</c>, <c>stopEngineWhenIdle: null</c>): satır pencerenin KENDİ tohumunu gözler — fixture'ın kapalı değerleri
    /// onu ezerdi. Anahtarı taşımayan durum <see cref="Every_reader_of_a_setting_starts_on_the_catalogs_default"/>'tedir.</summary>
    [StaTheory]
    [MemberData(nameof(WorkflowSettings))]
    public void The_shell_seeds_a_workflow_setting_and_persists_its_changes(GeneralSetting setting)
    {
        var chain = Of(setting);
        var workflow = chain.Workflow!;
        bool other = !DefaultOf(setting);
        using var temp = new TempDir();
        var saved = new UiState();
        chain.SetState(saved, other);
        UiStateStore(temp).Save(saved);

        var (window, vm) = New(temp, compactConsole: null, stopEngineWhenIdle: null);

        Assert.Equal(other, workflow.Vm(vm));
        workflow.SetVm(vm, !other);
        Assert.Equal<bool?>(!other, chain.State(UiStateStore(temp).Load()));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- realize (iş akışı tercihleri)

    /// <summary>
    /// Realize: satır General sayfasının tek <c>ToggleRow</c> şablonuyla çizilir (<c>Ds.Switch</c>, tooltip yok); diyalog canlı değerle
    /// açılır, switch taslağın bayrağıdır, Save'e kadar hiçbir şey uygulanmaz ve Save onu UiState'e ve VM'e götürür. İki yön de sınanır:
    /// Save'in varsayılan DIŞI bir değeri de, varsayılana dönüşü de uyguladığı.
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0 §2.9]</b> Pull before build'in eski iddiası
    /// (<c>The_section_header_carries_the_caps_label_the_switch_and_its_tooltip</c>): switch External projects sayfasının başındaki kural
    /// satırında, <c>PULL BEFORE BUILD</c> caps etiketi ve açıklama tooltip'iyle dururdu. Davranış anahtarları General'da toplandı; yeri
    /// değişti, bağı değişmedi.</para>
    /// <para><b>[DEĞİŞEN GEREKÇE — karar 16 → 47a]</b> İki yönün gerekçesi: <see cref="RunViewModel.ApplySettingsAsync"/>'in parametresi
    /// literal varsayılanlıyken varsayılana dönen yön, Save bayrağı atlasa da yeşil kalırdı. Parametreler artık <c>bool?</c>'dur ve
    /// verilmemesi "dokunma" demektir — iki yön de Save'in değeri taşıdığını ayırt eder.</para>
    /// </summary>
    [StaTheory]
    [MemberData(nameof(WorkflowSettingsBothWays))]
    public void The_general_page_realizes_a_workflow_switch_and_save_applies_it(GeneralSetting setting, bool opensOn)
    {
        var chain = Of(setting);
        var workflow = chain.Workflow!;
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized(r => workflow.SetVm(r, opensOn));
        using var _scope = scope;

        var toggle = SettingsDialogHost.GeneralSwitch(dialog, GeneralSettingsCatalog.Definition(setting).SwitchName);
        Assert.Equal(dialog.FindResource("Ds.Switch"), toggle.Style);
        Assert.Null(toggle.ToolTip);
        Assert.Equal(opensOn, toggle.IsChecked); // diyalog canlı değerle açılır

        toggle.IsChecked = !opensOn;
        Assert.Equal(!opensOn, workflow.Draft(dialog.Draft!));
        Assert.Equal(opensOn, workflow.Vm(run)); // Save'e kadar uygulanmaz

        CommandPress.Click(dialog.Save);

        Assert.Equal<bool?>(!opensOn, chain.State(store.State));
        DispatcherPump.PumpUntil(() => workflow.Vm(run) == !opensOn, TimeSpan.FromSeconds(5));
        Assert.Equal(!opensOn, workflow.Vm(run));
    }

    // ---------------------------------------------------------------- kabuk anahtarları: okuyucu, ui-state dosyası, Save

    /// <summary>Kayıtlı bir değer kataloğun varsayılanının ÖNÜNE geçer — hem <see cref="ShellSwitches.IsOn"/>'da hem anahtarın kendi
    /// okuyucusunda. (Hiç kaydedilmemiş anahtar varsayılanı okur: <see cref="Every_reader_of_a_setting_starts_on_the_catalogs_default"/>.)</summary>
    [Theory]
    [MemberData(nameof(ShellSettings))]
    public void A_saved_shell_switch_reads_its_saved_value(GeneralSetting setting)
    {
        var chain = Of(setting);
        bool other = !DefaultOf(setting);
        var state = new UiState();
        chain.SetState(state, other);

        Assert.Equal(other, ShellSwitches.IsOn(state, setting));
        Assert.Equal(other, chain.Shell!.Reads(state));
    }

    /// <summary>Diskteki AÇIK bir <c>null</c> token'ı (elle düzenleme) <see cref="UiState.UpdateExternals"/>'ın deseniyle tolere edilir: o
    /// anahtar kataloğun varsayılanına döner, diğer anahtarlar ve dosyadaki DİĞER alanlar (<see cref="UiState.LayoutMode"/>) KORUNUR.
    /// <c>Autostart</c> eskiden <c>bool</c>'du — bu token tüm dosyayı düşürüp yerleşimi sıfırlıyordu.</summary>
    [Theory]
    [MemberData(nameof(ShellSettings))]
    public void A_shell_switch_survives_the_json_store_and_a_null_token_reads_as_the_default(GeneralSetting setting)
    {
        var chain = Of(setting);
        using var temp = new TempDir();
        string path = UiStatePath(temp);
        var saved = new UiState { LayoutMode = LayoutMode.List };
        foreach (var c in Shell) c.SetState(saved, !DefaultOf(c.Setting)); // dört anahtar da varsayılan DIŞI
        new JsonUiStateStore(path).Save(saved);
        var before = new JsonUiStateStore(path).Load();
        Assert.All(Shell, c =>                                                 // kayıt diskten aynen döner, okuyucular onu okur
        {
            Assert.Equal<bool?>(!DefaultOf(c.Setting), c.State(before));
            Assert.Equal(!DefaultOf(c.Setting), c.Shell!.Reads(before));
        });

        string written = $"\"{chain.Shell!.StateToken}\": {Json(!DefaultOf(setting))}";
        string nulled = $"\"{chain.Shell.StateToken}\": null";
        File.WriteAllText(path, File.ReadAllText(path).Replace(written, nulled, StringComparison.Ordinal));
        Assert.Contains(nulled, File.ReadAllText(path), StringComparison.Ordinal); // ön-koşul: token gerçekten yazıldı
        var reloaded = new JsonUiStateStore(path).Load();

        Assert.Equal(DefaultOf(setting), ShellSwitches.IsOn(reloaded, setting));  // null token → katalog varsayılanı
        Assert.Equal(DefaultOf(setting), chain.Shell.Reads(reloaded));
        Assert.All(Shell.Where(c => c.Setting != setting), c =>                    // diğer anahtarlar KORUNUR
        {
            Assert.Equal(!DefaultOf(c.Setting), ShellSwitches.IsOn(reloaded, c.Setting));
            Assert.Equal(!DefaultOf(c.Setting), c.Shell!.Reads(reloaded));
        });
        Assert.Equal(LayoutMode.List, reloaded.LayoutMode);                         // dosyadaki diğer alanlar KORUNUR
    }

    /// <summary>
    /// Save değişen kabuk anahtarını yazar ve notunu BİR kez düşer; dokunulmayan anahtarlar varsayılanlarında yazılır ve sessiz kalır.
    /// Yeniden açılan diyalog kayıtlı değerle gelir, anahtar geri çevrilip kaydedilince o yön de yazılır ve notu bir kez düşer. "Değişti
    /// mi" sorusu önceki KAYITLI duruma göre sorulur (<see cref="ShellSwitches.Commit"/>). Windows yüzeyi yoktur (yalnız kalıcılık) —
    /// Start with Windows'un kaydı <see cref="StartWithWindowsTests"/>'tedir.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShellSettings))]
    public async Task A_changed_shell_switch_is_saved_and_noted_once_each_way_and_the_others_stay_quiet(GeneralSetting setting)
    {
        var chain = Of(setting);
        bool other = !DefaultOf(setting);
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = NewRun(engine);
        var store = new SettingsDialogHost.FakeStore();
        var others = Shell.Where(c => c.Setting != setting).ToList();

        var draft = new SettingsDraftViewModel(null, Root, saved: store.Load()); // diyaloğun açılışı
        draft.GeneralRow(setting).IsOn = other;
        await draft.CommitAsync(run, store);

        Assert.Equal<bool?>(other, chain.State(store.State));
        Assert.Equal(other, chain.Shell!.Reads(store.State));
        Assert.All(others, c =>
        {
            Assert.Equal<bool?>(DefaultOf(c.Setting), c.State(store.State)); // Save HER anahtarı yazar
            Assert.Equal(DefaultOf(c.Setting), c.Shell!.Reads(store.State));
        });
        Assert.Equal(1, Occurrences(run.GetRunDocumentText(), chain.Shell.Note(other)));
        Assert.All(others, c => Assert.DoesNotContain(c.Label, run.GetRunDocumentText(), StringComparison.Ordinal));

        var reopened = new SettingsDraftViewModel(null, Root, saved: store.Load());
        Assert.Equal(other, reopened.GeneralRow(setting).IsOn); // ön-koşul: kayıtlı değerle açıldı
        reopened.GeneralRow(setting).IsOn = !other;
        await reopened.CommitAsync(run, store);

        Assert.Equal<bool?>(!other, chain.State(store.State));
        Assert.Equal(!other, chain.Shell.Reads(store.State));
        Assert.Equal(1, Occurrences(run.GetRunDocumentText(), chain.Shell.Note(!other)));
        Assert.Equal(1, Occurrences(run.GetRunDocumentText(), chain.Shell.Note(other))); // ilk notun yanına ikincisi gelmedi
        Assert.All(others, c => Assert.DoesNotContain(c.Label, run.GetRunDocumentText(), StringComparison.Ordinal));
    }
}
