using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · I1] Proje log yüklemesinin motor tarafı — testlerin TEK modeli. İstek, VM'in motora gönderdiği
/// <c>getProjectLog</c>'tur; cevap, motorun yaptığı gibi (<c>SupervisorHost.SendProjectLogAsync</c>) o isteğin kimliğini
/// yankılar. Kimlik VM'in iç durumundan değil GÖNDERİLEN komuttan okunur: böylece testler gönderimin kendisini de sınar —
/// kimliği taşımayan bir istek hiçbir cevapla eşleşmez.
/// <para>Bir cevabın gelişi testin <c>vm.OnEvent(...)</c>'idir; motor cevapları istek sırasıyla yazar ve bir cevabın chunk'ları
/// arka arkadadır.</para>
/// </summary>
internal static class ProjectLogIpc
{
    /// <summary>Proje logunu ister (kart seçimi ÇAĞIRANIN işidir — üretimde seçim yüklemeden önce kurulur) ve motora giden isteği
    /// gözlemler: dönen görev yüklemenin awaiter'ı, kimlik motorun cevabında yankılanacak olandır. Testin kendi komut gözlemcisi
    /// (<c>DebugOnCommandSent</c>) varsa o da çağrılır ve yerine geri konur.</summary>
    public static (Task Load, int RequestId) Request(RunViewModel vm, string projectId)
    {
        ArgumentNullException.ThrowIfNull(vm);
        GetProjectLogCommand? request = null;
        var observer = vm.DebugOnCommandSent;
        vm.DebugOnCommandSent = command =>
        {
            observer?.Invoke(command);
            if (command is GetProjectLogCommand sent) request = sent;
        };
        Task load;
        try { load = vm.LoadProjectLogAsync(projectId); } // istek ilk await'ten ÖNCE, senkron gider
        finally { vm.DebugOnCommandSent = observer; }
        return (load, request?.RequestId ?? throw new InvalidOperationException("no getProjectLog was sent"));
    }

    /// <summary>Motorun cevabı: diskteki snapshot <paramref name="through"/>. satıra kadar <paramref name="text"/>'tir. Varsayılan tek
    /// chunk'lık cevaptır; çok chunk'lı cevabı test <paramref name="sequence"/> ve <paramref name="isLast"/> ile kurar (her chunk aynı
    /// kimliği ve aynı <paramref name="through"/>'u taşır).</summary>
    public static ProjectLogChunkEvent Reply(int requestId, string projectId, string text, int through,
        int sequence = 0, bool isLast = true) =>
        new(projectId, sequence, text, isLast, through, requestId);

    /// <summary>Motorun "log yok" cevabı: tek, boş, son chunk — <c>Through 0</c>, bayraklı (boş bir snapshot).</summary>
    public static ProjectLogChunkEvent NotFound(int requestId, string projectId) =>
        new(projectId, 0, "", IsLast: true, ThroughLineNumber: 0, requestId, NotFound: true);
}
