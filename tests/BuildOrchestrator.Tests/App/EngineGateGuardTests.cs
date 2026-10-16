using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 3 · O-3] Motor kapısının sınırı — <c>NoGitMutationOutsideTheWriterTests</c> deseni: kapıdan geçmiş bir isteğin TEK
/// gönderimi (<c>RunViewModel.SendWithFailureLineAsync</c>) yalnız kapıyı isteği için kendisi soran yöntemlerden çağrılır. Kapı
/// (<c>RunViewModel.EnsureEngineAwakeAsync</c>) uyku döngüsündeki motora giden isteği bekletir, motoru uyandırır ya da isteği geri çeker. Yeni bir
/// komut doğrudan <c>SendWithFailureLineAsync(...)</c> ile yazılırsa kapıyı sessizce atlar — uyuyan motora gönderim "Engine is not running."
/// satırıyla düşer, uyandırma hiç olmaz — ve yol tablosu testi (<c>EngineWakeTests</c>) bunu görmez: yalnız bilinen yolları sürer.
///
/// <para><b>[round 3] Zincirin alt halkaları da tek çağıranlıdır:</b> gönderimin tek yolu <c>SendToEngineAsync</c> (gözlemci + test dikişi +
/// motor) yalnız <c>SendWithFailureLineAsync</c>'ten, motorun <c>SendAsync</c>'i yalnız <c>SendToEngineAsync</c>'ten çağrılır — kapıyı bir alt
/// halkadan atlayan gönderim de kırmızıdır. Motorun gönderimi ALICIDAN BAĞIMSIZ taranır (<c>.SendAsync(</c>): <c>_engine</c> dışında bir
/// referansla motora giden gönderim de görülür; App'te motorla ilgisiz bir <c>SendAsync</c> doğarsa izin listesine gerekçesiyle girer.</para>
///
/// <para><b>[round 2 · T21 OOS-1] Kapının geçişi unutulamaz:</b> kapı bir geçiş döner (<c>RunViewModel.GatePass</c>); kapıda beklemiş bir isteğin
/// kaydı (<c>EngineGate</c> — kapının durumu, B1 fix 3) onunla yaşar ve motor kaybı kayıtlı isteğin penceresini bırakmaz. Her kapı çağrısı bir
/// <c>using</c> bildiriminin başlatıcısıdır — bir çağıran geçişi tutmayı unutursa kayıt hiç silinmez ve sonraki her kayıp o türün penceresini
/// atlar. [B1 fix 4] Kaydın kendisi de yalnız kapıdan açılır (<c>EngineGate.Enter</c> yalnız <c>EnsureEngineAwakeAsync</c>'ten, alıcıdan bağımsız
/// taranır): geçişe bağlanmayan bir kayıt hiç bırakılmaz, kurtaran uyanmanın tazelemesi de kapının boşalmasını sonsuza dek beklerdi.</para>
///
/// <para><b>[son temizlik · madde 10-11] Kaybın kapı soruları testlerin listesine bağlıdır:</b> motor kaybının geri alımındaki
/// <c>Holds(typeof(X))</c> soruları, pencere tutan türlerin listesiyle (<c>EngineWakeTests.WindowHolders</c> — davranışını kayıp teorisi sınar)
/// aynı kümedir; uyandıran ama kapıya hiç sormayan tek tür, branch envanteri, Sync'in geçişiyle ve kayıtsız gider.</para>
///
/// <para><b>İzin listesi DAR ve GEREKÇELİ:</b> gönderimin varsayılan yolu (kapı + tek gönderim), komutunu kapıdan sonra kuran koşu, Sync ile
/// ardındaki branch envanteri için kapıya BİR kez soran Sync ve yerini yenisi alabilen proje logu. Çağrı adedi PİNLENMEZ, çağıran yöntem listesi
/// pinlenir; yeni bir çağıran guard'ı kırmızıya çeker. Yöntemin kendi tanımı çağrı sayılmaz.</para>
///
/// <para><b>Çağrının sahibi</b> kendinden önceki son üye bildirimidir (<see cref="MemberDeclaration"/>): [round 3] yöntemin yanında özellik, alan,
/// olay ve iç içe tür de sınırdır — ifade gövdeli bir özellikteki ya da bir alanın başlatıcısındaki çağrı üstündeki (belki izinli) yönteme
/// sayılmaz.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınır):</b> izinli bir yöntemin İÇİNDE kapıya sormadan yapılan gönderim ve yöntemi bir temsilciye bağlayıp
/// başka yerden çağırmak — niyetin denetimi review'ın işidir. Adı devam satırında kalan ya da hiç belirteç taşımayan bir bildirim (arayüz üyesi
/// gibi) sınır sayılmaz: içindeki çağrı üstündeki üyeye sayılır. Tarama yorumları ve metin literallerini görmez
/// (<see cref="SourceLiterals.CodeOnly"/>): kuralı ANLATAN bir doküman satırı onu ihlal etmez.</para>
/// </summary>
public sealed class EngineGateGuardTests
{
    /// <summary>Kapıdan geçmiş bir isteğin gönderimi — çağrı biçimi.</summary>
    private static readonly Regex GatedSend = new(@"\bSendWithFailureLineAsync\s*\(", RegexOptions.Compiled);

    /// <summary>[round 3] Gönderimin tek yolu (gözlemci + test dikişi + motor) — çağrı biçimi.</summary>
    private static readonly Regex EngineRoute = new(@"\bSendToEngineAsync\s*\(", RegexOptions.Compiled);

    /// <summary>[round 3] Motorun gönderimi — alıcıdan BAĞIMSIZ: <c>_engine.SendAsync(</c>, <c>_engine?.SendAsync(</c> ya da motora giden başka bir
    /// referans.</summary>
    private static readonly Regex HostSend = new(@"\.\s*SendAsync\s*\(", RegexOptions.Compiled);

    /// <summary>Motor kapısının çağrısı.</summary>
    private static readonly Regex GateCall = new(@"\bEnsureEngineAwakeAsync\s*\(", RegexOptions.Compiled);

    /// <summary>[B1 fix 4] Kapıya kayıt — alıcıdan BAĞIMSIZ: <c>_engineGate.Enter(</c> ya da kapının durumuna giden başka bir referans. App'te
    /// kapıyla ilgisiz bir üye erişimli <c>Enter(</c> doğarsa izin listesine gerekçesiyle girer.</summary>
    private static readonly Regex GateEntry = new(@"\.\s*Enter\s*\(", RegexOptions.Compiled);

    /// <summary>[son temizlik · madde 10] Motor kaybının kapı sorusu — alıcıdan bağımsız, sorulan komut türüyle (<c>operand</c>).</summary>
    private static readonly Regex LossQuestion = new(@"\.\s*Holds\s*\(\s*typeof\s*\(\s*(?<operand>\w+)\s*\)\s*\)", RegexOptions.Compiled);

    /// <summary>[son temizlik · madde 11 · O-6] Branch envanterinin kurulumu.</summary>
    private static readonly Regex InventoryBuilt = new(@"\bnew\s+ListBranchesCommand\s*\(", RegexOptions.Compiled);

    /// <summary>[son temizlik · madde 11 · O-6] Branch envanterinin kapısız gönderimi: kapıdan geçmiş isteğin tek gönderimine, devredilecek kayıt
    /// olmadan (<c>GatePass.None</c>) verilir.</summary>
    private static readonly Regex InventorySentWithoutTheGate = new(
        @"\bSendWithFailureLineAsync\s*\(\s*new\s+ListBranchesCommand\s*\([^;]*,\s*GatePass\.None\s*\)\s*;", RegexOptions.Compiled);

    /// <summary>Çağrının bulunduğu satırda, çağrıdan hemen önce: geçişi tutan <c>using</c> bildirimi (<c>using var x = await</c> ya da
    /// <c>using (var x = await</c>).</summary>
    private static readonly Regex HeldWithUsing = new(@"using\s*(?:\(\s*)?var\s+\w+\s*=\s*await\s+$", RegexOptions.Compiled);

    /// <summary>Sınıf üyesinin bildirimi — çağrının sahibini belirleyen sınır: dosya kapsamlı namespace'te üyelerin girintisi (4 boşluk), en az bir
    /// erişim/biçim belirteci ve [round 3] üyenin her biçimi: yöntem ve kurucu (ad + parametre listesi; tuple dönüş türü dahil), özellik (ifade
    /// gövdeli ya da erişimci bloklu), alan ve olay (ad + <c>=</c> ya da <c>;</c>) ve iç içe tür (<c>class</c>/<c>struct</c>/<c>record</c>/…
    /// + ad). Ad, satırda ardından bunlardan biri gelen İLK addır. Yerel fonksiyonlar ve lambdalar daha derindedir ve belirteç taşımaz — içlerindeki
    /// çağrı, içinde bulundukları üyeye sayılır; iç içe türün üyeleri de daha derindedir — içlerindeki çağrı o türe sayılır.</summary>
    private static readonly Regex MemberDeclaration = new(
        @"^ {4}(?:(?:public|private|internal|protected|static|async|override|sealed|virtual|partial|readonly|extern|unsafe|const|event|required|new"
        + @"|abstract|volatile)\s+)+"
        + @"(?:(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+(?<name>\w+)"
        + @"|[^=;{}\r\n]*?\b(?<name>\w+)\s*(?:<[^>\r\n]*>)?\s*(?:\(|=|\{|;))",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Kapıyı isteği için kendisi soran yöntemler — gönderimin meşru olduğu TEK yerler ve gerekçeleri.</summary>
    private static readonly IReadOnlyCollection<string> GatedCallers =
    [
        "TrySendAsync",        // gönderimin varsayılan yolu: kapı + tek gönderim (Clean, Optimize, Stop, perf chip, checkout, pull)
        "BeginRunAsync",       // koşu: kapıya tıklamada sorar, komutunu kapıdan ve koreografiden SONRA kurar
        "SyncCoreAsync",       // Sync ve ardındaki branch envanteri: aynı isteğin iki parçası, kapıya TEK soru
        "LoadProjectLogAsync", // proje logu: kapının koşulu "yerini yenisi almadı"
    ];

    /// <summary>[round 3] Gönderimin tek yolunun TEK çağıranı: kapıdan geçmiş isteğin tek gönderimi.</summary>
    private static readonly IReadOnlyCollection<string> EngineRouteCallers = ["SendWithFailureLineAsync"];

    /// <summary>[round 3] Motorun gönderiminin TEK çağıranı: gözlemciye bildiren, test dikişine ya da motora yönlendiren yol.</summary>
    private static readonly IReadOnlyCollection<string> HostSendCallers = ["SendToEngineAsync"];

    /// <summary>[B1 fix 4] Kapıya kaydın TEK çağıranı: kaydı geçişe (<c>GatePass</c>) bağlayan kapının kendisi. Başka bir yerden açılan kayıt hiç
    /// bırakılmaz — motor kaybı o türün penceresini bir daha bırakmaz, kurtaran uyanmanın tazelemesi kapının boşalmasını sonsuza dek bekler.</summary>
    private static readonly IReadOnlyCollection<string> GateEntryCallers = ["EnsureEngineAwakeAsync"];

    /// <summary>Kaynaktaki bir çağrı: <c>dosya:satır</c>, içinde bulunduğu üye, geçişi tutan bir <c>using</c>'in başlatıcısı olup olmadığı ve
    /// — kalıp <c>operand</c> grubu taşıyorsa — sorulan şey (yoksa boş).</summary>
    private readonly record struct Call(string Where, string Member, bool Held, string Operand);

    [Fact]
    public void Only_the_gated_methods_call_the_gated_send() =>
        AssertCalledOnlyFrom(GatedSend, GatedCallers, "Motor kapısını atlayan gönderim (SendWithFailureLineAsync kapıya sormayan bir yöntemde):");

    /// <summary>[round 3] Kapı alt halkadan da atlanamaz: <c>SendToEngineAsync</c> yalnız <c>SendWithFailureLineAsync</c>'ten çağrılır.</summary>
    [Fact]
    public void Only_the_single_send_calls_the_engine_route() =>
        AssertCalledOnlyFrom(EngineRoute, EngineRouteCallers, "Motor kapısını atlayan gönderim (SendToEngineAsync, SendWithFailureLineAsync dışında):");

    /// <summary>[round 3] Kapı motorun kendisinden de atlanamaz: motorun <c>SendAsync</c>'i — hangi referansla olursa olsun — yalnız
    /// <c>SendToEngineAsync</c>'ten çağrılır.</summary>
    [Fact]
    public void Only_the_engine_route_calls_the_hosts_send() =>
        AssertCalledOnlyFrom(HostSend, HostSendCallers, "Motor kapısını atlayan gönderim (SendAsync, SendToEngineAsync dışında):");

    /// <summary>Vakum karşıtı: tarama izinli yöntemlerin her birinde en az bir gönderim ve en az bir kapı çağrısı görür — boş bir tarama guard'ı
    /// sessizce yeşil bırakırdı.</summary>
    [Fact]
    public void The_guard_sees_the_send_and_the_gate_in_every_gated_method()
    {
        var senders = AppCalls(GatedSend).Select(c => c.Member).ToHashSet();
        var askers = AppCalls(GateCall).Select(c => c.Member).ToHashSet();

        Assert.All(GatedCallers, caller => Assert.Contains(caller, senders));
        Assert.All(GatedCallers, caller => Assert.Contains(caller, askers));
    }

    /// <summary>[round 3] Vakum karşıtı: tarama gönderim zincirinin iki alt halkasını tek çağıranlarında görür.</summary>
    [Fact]
    public void The_guard_sees_every_link_of_the_send_chain()
    {
        var routeCallers = AppCalls(EngineRoute).Select(c => c.Member).ToHashSet();
        var hostSendCallers = AppCalls(HostSend).Select(c => c.Member).ToHashSet();

        Assert.All(EngineRouteCallers, caller => Assert.Contains(caller, routeCallers));
        Assert.All(HostSendCallers, caller => Assert.Contains(caller, hostSendCallers));
    }

    /// <summary>[B1 fix 4] Kapıya kayıt yalnız kapının kendisinden açılır: <c>EngineGate.Enter</c> — hangi referansla olursa olsun — yalnız
    /// <c>EnsureEngineAwakeAsync</c>'ten çağrılır; orada kayıt geçişe bağlanır ve geçiş <c>using</c> ile tutulur
    /// (<see cref="Every_gate_pass_is_held_with_using"/>), yani her kayıt teslimde ya da kapsamın sonunda biter.</summary>
    [Fact]
    public void Only_the_gate_enters_the_gate() =>
        AssertCalledOnlyFrom(GateEntry, GateEntryCallers, "Geçişsiz kapı kaydı (EngineGate.Enter, EnsureEngineAwakeAsync dışında):");

    /// <summary>[B1 fix 4] Vakum karşıtı: tarama kapıya kaydı tek çağıranında görür.</summary>
    [Fact]
    public void The_guard_sees_the_gate_entry() =>
        Assert.All(GateEntryCallers, caller => Assert.Contains(caller, AppCalls(GateEntry).Select(c => c.Member)));

    /// <summary>[round 2 · T21 OOS-1] Kapının her çağrısı geçişi bir <c>using</c> ile tutar: kayıt, istek motora teslim edilince ya da kapsamın
    /// sonunda bırakılır — hiçbir yolda sızamaz.</summary>
    [Fact]
    public void Every_gate_pass_is_held_with_using() =>
        AssertNoOffenders(AppCalls(GateCall).Where(c => !c.Held), "Kapının geçişi using ile tutulmuyor (kayıt sızabilir):");

    /// <summary>
    /// [son temizlik · madde 10 · M-2 + O-3] Motor kaybı kapıya TAM OLARAK pencere tutan türler için sorar: <c>ReleaseAfterEngineLoss</c>'taki
    /// <c>Holds(typeof(X))</c> sorularının türleri, pencere tutan türlerin listesiyle (<c>EngineWakeTests.WindowHolders</c>) aynı kümedir. Kayıp
    /// teorisi listenin her satırını davranışla sınar; bu tarama listeyi kaynağa bağlar — yedinci bir soru, istisna bir tür
    /// (<c>GetProjectLogCommand</c>, <c>ListBranchesCommand</c>) için eklenen soru, silinen ya da çiftlenen bir soru kırmızıdır.
    /// <para>Mutasyon: <c>ReleaseAfterEngineLoss</c>'a <c>Holds(typeof(GetProjectLogCommand))</c> sorusu eklenince kırmızı.</para>
    /// </summary>
    [Fact]
    public void The_engine_loss_asks_the_gate_exactly_for_the_window_holding_types()
    {
        var asked = AppCalls(LossQuestion).Where(c => c.Member == "ReleaseAfterEngineLoss").Select(c => c.Operand);

        Assert.Equal(EngineWakeTests.WindowHolders.Select(holder => holder.Command.Name).Order(), asked.Order());
    }

    /// <summary>
    /// [son temizlik · madde 11 · O-6] Branch envanteri kapıya HİÇ sormaz: <c>ListBranchesCommand</c> App'te yalnız Sync'in kapısından geçen
    /// <c>SyncCoreAsync</c>'te kurulur ve devredilecek kayıt olmadan (<c>GatePass.None</c>) gönderilir — kapının kararı ve kaydı Sync'in
    /// geçişindeydi. Kendi kapı sorusu olsaydı (<c>TrySendAsync</c> ya da <c>EnsureEngineAwakeAsync</c>) kendi penceresi de olurdu ve motor kaybı
    /// onun için de kapıya sormalıydı. Uyandıran türlerin istisna listesindeki gerekçenin pini (<c>EngineWakeTests.WakingWithoutAGatedWindow</c>).
    /// <para>Mutasyon: envanterin gönderiminde <c>GatePass.None</c> yerine Sync'in geçişi verilince kırmızı.</para>
    /// </summary>
    [Fact]
    public void The_branch_inventory_never_asks_the_gate()
    {
        var built = AppCalls(InventoryBuilt);

        Assert.NotEmpty(built); // vakum karşıtı
        Assert.All(built, call => Assert.Equal("SyncCoreAsync", call.Member));
        Assert.Equal(built.Count, AppCalls(InventorySentWithoutTheGate).Count); // her kurulum kapısız gönderimin içindedir
    }

    /// <summary>Guard'ın kendi kanıtı: kapıya sormayan bir yöntemdeki gönderim raporlanır; tanımın kendisi, yorum ve yerel fonksiyon raporlanmaz
    /// (yerel fonksiyonun içindeki çağrı içinde bulunduğu üyeye sayılır).</summary>
    [Fact]
    public void The_rule_recognises_a_send_that_bypasses_the_gate()
    {
        var calls = CallsIn(@"ViewModels\RunViewModel.Fake.cs", """
            public sealed partial class RunViewModel
            {
                private async Task<bool> SendWithFailureLineAsync(IpcCommand cmd, string failureLine, GatePass pass) => await Task.FromResult(true);

                /// <summary>Kural anlatılır: SendWithFailureLineAsync(...) yalnız kapıdan sonra.</summary>
                private async Task FutureCommandAsync()
                {
                    bool Local() => true;
                    if (Local()) await SendWithFailureLineAsync(new PingCommand(1), "[error] failed to send ping", GatePass.None);
                }
            }
            """, GatedSend);

        var call = Assert.Single(calls);
        Assert.Equal("FutureCommandAsync", call.Member);
        Assert.Equal(@"ViewModels\RunViewModel.Fake.cs:9", call.Where);
    }

    /// <summary>Guard'ın kendi kanıtı: geçişi <c>using</c> ile tutmayan kapı çağrısı raporlanır; <c>using var</c> ve <c>using (var …)</c> biçimleri,
    /// tanımın kendisi ve yorum raporlanmaz.</summary>
    [Fact]
    public void The_rule_recognises_a_gate_pass_that_is_not_held()
    {
        var calls = CallsIn(@"ViewModels\RunViewModel.Fake.cs", """
            public sealed partial class RunViewModel
            {
                private async Task<GatePass> EnsureEngineAwakeAsync(Type command) => await Task.FromResult(default(GatePass));

                private async Task HeldAsync()
                {
                    using var pass = await EnsureEngineAwakeAsync(typeof(PingCommand));
                    using (var other = await EnsureEngineAwakeAsync(typeof(PingCommand))) { }
                }

                /// <summary>Kural anlatılır: EnsureEngineAwakeAsync(...) using ile tutulur.</summary>
                private async Task ForgottenAsync()
                {
                    var pass = await EnsureEngineAwakeAsync(typeof(PingCommand));
                }
            }
            """, GateCall);

        Assert.Equal(3, calls.Count);
        var forgotten = Assert.Single(calls, c => !c.Held);
        Assert.Equal("ForgottenAsync", forgotten.Member);
        Assert.Equal(@"ViewModels\RunViewModel.Fake.cs:14", forgotten.Where);
    }

    /// <summary>[round 3] Guard'ın kendi kanıtı: motorun gönderimi alıcıdan bağımsız görülür — tek yolun kendi gönderimi izinlidir, başka bir
    /// referansla yapılan gönderim raporlanır; yorum raporlanmaz.</summary>
    [Fact]
    public void The_rule_recognises_a_host_send_through_any_reference()
    {
        var calls = CallsIn(@"ViewModels\RunViewModel.Fake.cs", """
            public sealed partial class RunViewModel
            {
                private Task SendToEngineAsync(IpcCommand cmd) => _engine?.SendAsync(cmd) ?? Task.CompletedTask;

                /// <summary>Kural anlatılır: _engine.SendAsync(...) yalnız SendToEngineAsync'ten.</summary>
                private async Task StrayAsync(EngineHost host)
                {
                    await host.SendAsync(new PingCommand(1));
                }
            }
            """, HostSend);

        Assert.Equal(2, calls.Count);
        var stray = Assert.Single(calls, c => !HostSendCallers.Contains(c.Member));
        Assert.Equal("StrayAsync", stray.Member);
        Assert.Equal(@"ViewModels\RunViewModel.Fake.cs:8", stray.Where);
    }

    /// <summary>[B1 fix 4] Guard'ın kendi kanıtı: kapıya kayıt alıcıdan bağımsız görülür — kapının kendi kaydı izinlidir, başka bir referansla açılan
    /// kayıt raporlanır; üye erişimi olmayan aynı adlı bir yöntem çağrısı (<c>Enter(…)</c>, ör. koreografinin kendi adımı) ve yorum raporlanmaz.</summary>
    [Fact]
    public void The_rule_recognises_a_gate_entry_through_any_reference()
    {
        var calls = CallsIn(@"ViewModels\RunViewModel.Fake.cs", """
            public sealed partial class RunViewModel
            {
                private async Task<GatePass> EnsureEngineAwakeAsync(Type command)
                {
                    int ticket = _engineGate.Enter(command);
                    return await Task.FromResult(default(GatePass));
                }

                /// <summary>Kural anlatılır: _engineGate.Enter(...) yalnız kapıdan.</summary>
                private void Stray(EngineGate gate)
                {
                    gate.Enter(typeof(PingCommand));
                    Enter(1);
                }
            }
            """, GateEntry);

        Assert.Equal(2, calls.Count);
        var stray = Assert.Single(calls, c => !GateEntryCallers.Contains(c.Member));
        Assert.Equal("Stray", stray.Member);
        Assert.Equal(@"ViewModels\RunViewModel.Fake.cs:12", stray.Where);
    }

    /// <summary>[round 3] Guard'ın kendi kanıtı — çağrının sahibi: ifade gövdeli bir özellikteki ve bir alanın başlatıcısındaki çağrı o üyeye
    /// sayılır, üstündeki (izinli) yönteme değil; iç içe bir türün içindeki çağrı o türe sayılır.
    /// <para>KIRMIZI (round 2 bildirim kalıbı yalnız parametre listeli üyeleri sınır sayıyordu): dört çağrının dördü de üstteki
    /// <c>SendWithFailureLineAsync</c>'e sayıldı — özellikteki gönderim izinli görünürdü.</para></summary>
    [Fact]
    public void The_rule_attributes_a_call_in_a_property_or_a_field_to_that_member()
    {
        var calls = CallsIn(@"ViewModels\RunViewModel.Fake.cs", """
            public sealed partial class RunViewModel
            {
                private async Task<bool> SendWithFailureLineAsync(IpcCommand cmd, string failureLine, GatePass pass)
                {
                    await SendToEngineAsync(cmd);
                    return true;
                }

                private Task Ping => SendToEngineAsync(new PingCommand(1));

                private readonly Func<Task> _pong = () => SendToEngineAsync(new PingCommand(2));

                private sealed class Nested
                {
                    public Task SendAsync() => SendToEngineAsync(new PingCommand(3));
                }
            }
            """, EngineRoute);

        Assert.Equal(["SendWithFailureLineAsync", "Ping", "_pong", "Nested"], calls.Select(c => c.Member));
    }

    /// <summary>App kaynağında çağrının yalnız izinli üyelerden yapıldığını doğrular.</summary>
    private static void AssertCalledOnlyFrom(Regex call, IReadOnlyCollection<string> callers, string message) =>
        AssertNoOffenders(AppCalls(call).Where(c => !callers.Contains(c.Member)), message);

    /// <summary>İhlal yoksa geçer; varsa her birini <c>dosya:satır (üye)</c> olarak listeler.</summary>
    private static void AssertNoOffenders(IEnumerable<Call> offenders, string message)
    {
        var lines = offenders.Select(c => $"{c.Where} ({c.Member})").ToList();

        Assert.True(lines.Count == 0, message + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", lines));
    }

    /// <summary>App kaynağındaki bütün çağrılar.</summary>
    private static List<Call> AppCalls(Regex call) =>
        [.. RepoPaths.AppSourceFiles("*.cs")
            .SelectMany(f => CallsIn(Path.GetRelativePath(RepoPaths.AppSrcRoot, f), File.ReadAllText(f), call))];

    /// <summary>Bir dosyadaki her çağrı: <c>dosya:satır</c>, içinde bulunduğu üye ve geçişi tutan bir <c>using</c>'in başlatıcısı olup olmadığı.
    /// Yorum ve literaller görülmez; tanımın kendisi çağrı sayılmaz.</summary>
    private static List<Call> CallsIn(string relative, string text, Regex call)
    {
        string code = SourceLiterals.CodeOnly(text);
        var members = MemberDeclaration.Matches(code).Select(m => (Start: m.Index, Name: m.Groups["name"])).ToList();
        var calls = new List<Call>();
        foreach (Match match in call.Matches(code))
        {
            if (members.Any(m => m.Name.Index == match.Index)) continue; // tanımın kendisi
            var owner = members.LastOrDefault(m => m.Start < match.Index).Name?.Value ?? "(üye dışında)";
            int line = code.AsSpan(0, match.Index).Count('\n') + 1;
            int lineStart = code.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
            bool held = HeldWithUsing.IsMatch(code[lineStart..match.Index]);
            string operand = match.Groups["operand"] is { Success: true } asked ? asked.Value : "";
            calls.Add(new Call($"{relative}:{line}", owner, held, operand));
        }
        return calls;
    }
}
