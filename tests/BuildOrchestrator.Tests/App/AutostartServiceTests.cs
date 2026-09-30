using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E2/T16] <see cref="AutostartService"/> — <c>UiState.Autostart</c> tercihini registry seam'iyle uzlaştırır.
/// Testler GERÇEK <c>HKCU\...\Run</c>'a ASLA yazmaz: <see cref="IAutostartRegistry"/> ortak bellek-içi
/// <see cref="FakeAutostartRegistry"/> ile doğrulanır.
/// </summary>
public class AutostartServiceTests
{
    [Fact]
    public void Apply_enabled_writes_the_run_value_with_the_injected_command()
    {
        var reg = new FakeAutostartRegistry();
        var svc = new AutostartService(reg, "BuildOrchestrator", @"C:\app\BuildOrchestrator.App.exe --autostart");

        svc.Apply(autostartEnabled: true);

        Assert.True(reg.Exists("BuildOrchestrator"));
        Assert.Equal(@"C:\app\BuildOrchestrator.App.exe --autostart", reg.CommandFor("BuildOrchestrator"));
    }

    [Fact]
    public void Apply_disabled_removes_the_run_value()
    {
        var reg = new FakeAutostartRegistry();
        var svc = new AutostartService(reg, "BuildOrchestrator", "cmd");
        svc.Apply(autostartEnabled: true);

        svc.Apply(autostartEnabled: false);

        Assert.False(reg.Exists("BuildOrchestrator"));
    }

    [Fact]
    public void Apply_is_idempotent_for_repeated_enable_and_disable()
    {
        var reg = new FakeAutostartRegistry();
        var svc = new AutostartService(reg, "BuildOrchestrator", "cmd");

        svc.Apply(true);
        svc.Apply(true);
        Assert.True(reg.Exists("BuildOrchestrator"));

        svc.Apply(false);
        svc.Apply(false);
        Assert.False(reg.Exists("BuildOrchestrator"));
    }

    [Fact]
    public void Default_value_name_is_stable()
    {
        Assert.Equal("BuildOrchestrator", AutostartService.DefaultValueName);
    }

    /// <summary>[yayın hattı · Task 2] Kaldırma kancası: Velopack uninstall'da Windows başlangıç kaydı silinir; registry
    /// reddederse kanca fırlatmaz (Velopack kancayı 30 s içinde bitmemişse öldürür, hata göstermez). Reddeden registry
    /// için ayrı bir fake YAZILMAZ (kopya YASAK): ortak <see cref="FakeAutostartRegistry.FailWritesWith"/> kancası
    /// <c>Remove</c>'u da fırlattırır.</summary>
    [Fact]
    public void RemoveForUninstall_deletes_the_run_value_and_swallows_a_registry_refusal()
    {
        var registry = new FakeAutostartRegistry();
        registry.Set(AutostartService.DefaultValueName, FakeAutostartRegistry.Command);
        AutostartService.RemoveForUninstall(registry);
        Assert.False(registry.Exists(AutostartService.DefaultValueName));

        var refusing = new FakeAutostartRegistry();
        refusing.Set(AutostartService.DefaultValueName, FakeAutostartRegistry.Command);
        refusing.FailWritesWith = new UnauthorizedAccessException("denied"); // Remove → UnauthorizedAccessException
        Assert.Null(Record.Exception(() => AutostartService.RemoveForUninstall(refusing))); // fırlatmaz
        Assert.True(refusing.Exists(AutostartService.DefaultValueName)); // reddedildi → kayıt yerinde kaldı (yutuldu, silinmedi)
    }

    /// <summary>[P4] Açılışın uzlaştırması (<c>App.OnStartup</c>) Windows kaydı yazılamadığında uygulamayı
    /// DÜŞÜRMEZ — bir politika ya da güvenlik yazılımı <c>HKCU\...\Run</c>'ı kilitlemiş olabilir; kayıt bir
    /// sonraki açılışta yeniden denenir.</summary>
    [Fact]
    public void The_startup_reconcile_survives_a_registry_that_refuses_the_write()
    {
        var reg = new FakeAutostartRegistry { FailWritesWith = new UnauthorizedAccessException("denied") };
        var svc = reg.Service();

        Assert.Null(Record.Exception(() => svc.Apply(true)));
        Assert.Null(Record.Exception(() => svc.Apply(false)));
        Assert.False(reg.Exists(AutostartService.DefaultValueName));
    }

    /// <summary>[P4 · pin] Run komutu: exe yolu TIRNAKLI (boşluklu bir kurulum klasörü komutu bölmesin) ve
    /// "Windows ile açıldım" işareti olan autostart argümanı sonda. Mevcut biçimin pini — komutun kurulduğu tek
    /// yer (<c>App.AutostartCommandFor</c>) test edilebilsin diye saf bir yardımcıya ayrıldı.</summary>
    [Fact]
    public void The_run_command_quotes_the_exe_path_and_ends_with_the_autostart_marker()
    {
        Assert.Equal("\"C:\\Program Files\\Build Orchestrator\\BuildOrchestrator.App.exe\" --autostart",
            BuildOrchestrator.App.App.AutostartCommandFor(@"C:\Program Files\Build Orchestrator\BuildOrchestrator.App.exe"));
    }
}
