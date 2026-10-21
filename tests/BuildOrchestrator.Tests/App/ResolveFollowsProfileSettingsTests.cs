using System.IO;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Resolve cycles de her koşu gibi perf çipinin cap'ini ve priority'sini aynen uygular; Settings → General'da buna dair bir anahtar yoktur.
/// <para><b>[DEĞİŞEN KURAL — 2026-10-17, kullanıcı kararı]</b> Eski iddia (RESOLVE Faz 4 / karar 11 — <c>ResolveAtFullPrioritySettingsTests</c>):
/// BUILD grubundaki <c>Resolve cycles at full priority</c> anahtarı açıkken (varsayılan) Resolve cycles profilin işçi sayısını korur ama
/// cap'siz ve Normal öncelikte koşardı; değer <c>startRun</c> ile motora giderdi, koşu içi çip notu ve run başı satırı bunu anlatırdı.
/// Anahtar ve kural bütün katmanlardan kalktı. Gerekçe, 2026-10-17 ölçümü (gerçek OSYS, Balanced 4 işçi, 25 proje): sakin makinede kural
/// kazandırmadı (56,5 sn / 52,4 sn); yüklü makinede %5 kazandırdı, ama arayüzde 250 ms üstü 23 kare boşluğu açtı, en uzunu 5,2 sn (cap'li
/// BelowNormal: 2 boşluk, en uzun 1,6 sn). Tek düğme ilkesi: cap ve priority'yi yalnız perf çipi belirler.</para>
/// <para>Burada kalan: anahtarın katalogda satırı olmadığı ve onu yazmış eski kayıtların (ui-state, ayar dosyası) hata vermeden okunduğu.
/// Eski <c>startRun</c> satırının toleransı <c>IpcMessagesTests</c>'te; motorun profili Resolve'da da aynen uygulaması
/// <c>RunCoordinatorTests</c>'te; koşu içi çip notunun tek biçimi <c>PerfProfileTests</c> ve <c>RunViewModelStateTests</c>'te.</para>
/// </summary>
public class ResolveFollowsProfileSettingsTests
{
    /// <summary>Katalogda Resolve'a özgü bir satır yok; BUILD grubu yalnız <c>Pull before build</c> taşır. Satıra ETİKETLE bakılır, enum
    /// üyesiyle değil: etiket kataloğun kullanıcıya görünen kimliğidir ve üye kalksa da kalkmasa da derlenen bir test kırmızıyı da gösterir.</summary>
    [Fact]
    public void The_general_catalog_has_no_resolve_priority_row()
    {
        Assert.DoesNotContain(GeneralSettingsCatalog.Groups.SelectMany(g => g.Rows), r => r.Label == "Resolve cycles at full priority");

        var build = Assert.Single(GeneralSettingsCatalog.Groups, g => g.Title == "BUILD");
        Assert.Equal([GeneralSetting.PullBeforeBuild], build.Rows.Select(r => r.Setting));
    }

    /// <summary>Kalkan anahtarı yazmış eski bir <c>ui-state.json</c> yüklenir: fazla alan yok sayılır, dosyadaki öteki değerler korunur
    /// (yüklenemeseydi <see cref="JsonUiStateStore.Load"/> sessizce boş duruma düşer ve kullanıcının tercihleri sıfırlanırdı).</summary>
    [Fact]
    public void An_old_ui_state_with_the_resolve_priority_field_still_loads()
    {
        using var temp = new TempDir();
        File.WriteAllText(UiStatePath(temp), """{"PerfMode":"Light","ResolveAtFullPriority":false}""");

        var state = UiStateStore(temp).Load();

        Assert.Equal("Light", state.PerfMode);
    }

    /// <summary>Kalkan anahtarı taşıyan eski bir ayar dosyası içe aktarılır: fazla alan yok sayılır, dosyanın taşıdığı öteki ayar (Stash
    /// and switch branches, varsayılanı kapalı) taslağa uygulanır. Import yolu: <see cref="SettingsFile.TryParse"/> + <see cref="SettingsDraftViewModel.LoadFrom"/>.</summary>
    [Fact]
    public void An_old_settings_file_with_the_resolve_priority_field_still_imports()
    {
        const string json = """
            {"app":"Build Orchestrator","version":"1.10.1","repositoryRoot":"D:\\repo",
             "stashOnBranchSwitch":true,"resolveAtFullPriority":false,"layers":[]}
            """;
        var draft = new SettingsDraftViewModel(null, @"D:\repo");

        var file = SettingsFile.TryParse(json);
        Assert.NotNull(file); // geçerli dosya: "Invalid settings file" ile reddedilmedi
        draft.LoadFrom(file);

        Assert.True(draft.StashOnBranchSwitch);
    }
}
