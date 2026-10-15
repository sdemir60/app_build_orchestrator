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
/// <para><b>İzin listesi DAR ve GEREKÇELİ:</b> gönderimin varsayılan yolu (kapı + tek gönderim), komutunu kapıdan sonra kuran koşu, Sync ile
/// ardındaki branch envanteri için kapıya BİR kez soran Sync ve yerini yenisi alabilen proje logu. Çağrı adedi PİNLENMEZ, çağıran yöntem listesi
/// pinlenir; yeni bir çağıran guard'ı kırmızıya çeker. Yöntemin kendi tanımı çağrı sayılmaz.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınır):</b> izinli bir yöntemin İÇİNDE kapıya sormadan yapılan gönderim ve yöntemi bir temsilciye bağlayıp
/// başka yerden çağırmak — niyetin denetimi review'ın işidir. Tarama yorumları ve metin literallerini görmez
/// (<see cref="SourceLiterals.CodeOnly"/>): kuralı ANLATAN bir doküman satırı onu ihlal etmez.</para>
/// </summary>
public sealed class EngineGateGuardTests
{
    /// <summary>Kapıdan geçmiş bir isteğin gönderimi — çağrı biçimi.</summary>
    private static readonly Regex GatedSend = new(@"\bSendWithFailureLineAsync\s*\(", RegexOptions.Compiled);

    /// <summary>Sınıf üyesinin bildirimi: dosya kapsamlı namespace'te üyelerin girintisi (4 boşluk), en az bir erişim/biçim belirteci, dönüş türü,
    /// ad ve parametre listesi. Yerel fonksiyonlar ve lambdalar daha derindedir ve belirteç taşımaz — içlerindeki çağrı, içinde bulundukları üyeye
    /// sayılır.</summary>
    private static readonly Regex MemberDeclaration = new(
        @"^ {4}(?:(?:public|private|internal|protected|static|async|override|sealed|virtual|partial|readonly|extern|unsafe)\s+)+"
        + @"[^=;{}()\r\n]*?\b(?<name>\w+)\s*(?:<[^>\r\n]*>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Kapıyı isteği için kendisi soran yöntemler — gönderimin meşru olduğu TEK yerler ve gerekçeleri.</summary>
    private static readonly IReadOnlyCollection<string> GatedCallers =
    [
        "TrySendAsync",        // gönderimin varsayılan yolu: kapı + tek gönderim (Clean, Optimize, Stop, perf chip, checkout, pull)
        "BeginRunAsync",       // koşu: kapıya tıklamada sorar, komutunu kapıdan ve koreografiden SONRA kurar
        "SyncCoreAsync",       // Sync ve ardındaki branch envanteri: aynı isteğin iki parçası, kapıya TEK soru
        "LoadProjectLogAsync", // proje logu: kapının koşulu "yerini yenisi almadı"
    ];

    [Fact]
    public void Only_the_gated_methods_call_the_gated_send()
    {
        var offenders = AppCalls().Where(c => !GatedCallers.Contains(c.Member)).Select(c => $"{c.Where} ({c.Member})").ToList();

        Assert.True(offenders.Count == 0,
            "Motor kapısını atlayan gönderim (SendWithFailureLineAsync kapıya sormayan bir yöntemde):" + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", offenders));
    }

    /// <summary>Vakum karşıtı: tarama izinli yöntemlerin her birinde en az bir çağrı görür — boş bir tarama guard'ı sessizce yeşil bırakırdı.</summary>
    [Fact]
    public void The_guard_sees_the_send_in_every_gated_method()
    {
        var callers = AppCalls().Select(c => c.Member).ToHashSet();

        Assert.All(GatedCallers, caller => Assert.Contains(caller, callers));
    }

    /// <summary>Guard'ın kendi kanıtı: kapıya sormayan bir yöntemdeki gönderim raporlanır; tanımın kendisi, yorum ve yerel fonksiyon raporlanmaz
    /// (yerel fonksiyonun içindeki çağrı içinde bulunduğu üyeye sayılır).</summary>
    [Fact]
    public void The_rule_recognises_a_send_that_bypasses_the_gate()
    {
        var calls = CallsIn(@"ViewModels\RunViewModel.Fake.cs", """
            public sealed partial class RunViewModel
            {
                private async Task<bool> SendWithFailureLineAsync(IpcCommand cmd, string failureLine) => await Task.FromResult(true);

                /// <summary>Kural anlatılır: SendWithFailureLineAsync(...) yalnız kapıdan sonra.</summary>
                private async Task FutureCommandAsync()
                {
                    bool Local() => true;
                    if (Local()) await SendWithFailureLineAsync(new PingCommand(1), "[error] failed to send ping");
                }
            }
            """);

        var call = Assert.Single(calls);
        Assert.Equal("FutureCommandAsync", call.Member);
        Assert.Equal(@"ViewModels\RunViewModel.Fake.cs:9", call.Where);
    }

    /// <summary>App kaynağındaki bütün çağrılar.</summary>
    private static List<(string Where, string Member)> AppCalls() =>
        [.. RepoPaths.AppSourceFiles("*.cs")
            .SelectMany(f => CallsIn(Path.GetRelativePath(RepoPaths.AppSrcRoot, f), File.ReadAllText(f)))];

    /// <summary>Bir dosyadaki her <c>SendWithFailureLineAsync(</c> çağrısı: <c>dosya:satır</c> ve içinde bulunduğu üye. Yorum ve literaller
    /// görülmez; tanımın kendisi çağrı sayılmaz.</summary>
    private static List<(string Where, string Member)> CallsIn(string relative, string text)
    {
        string code = SourceLiterals.CodeOnly(text);
        var members = MemberDeclaration.Matches(code).Select(m => (Start: m.Index, Name: m.Groups["name"])).ToList();
        var calls = new List<(string Where, string Member)>();
        foreach (Match call in GatedSend.Matches(code))
        {
            if (members.Any(m => m.Name.Index == call.Index)) continue; // tanımın kendisi
            var owner = members.LastOrDefault(m => m.Start < call.Index).Name?.Value ?? "(üye dışında)";
            int line = code.AsSpan(0, call.Index).Count('\n') + 1;
            calls.Add(($"{relative}:{line}", owner));
        }
        return calls;
    }
}
