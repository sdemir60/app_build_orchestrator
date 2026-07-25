using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E2/T16] <see cref="AutostartService"/> — <c>UiState.Autostart</c> tercihini registry seam'iyle uzlaştırır.
/// Testler GERÇEK <c>HKCU\...\Run</c>'a ASLA yazmaz: <see cref="IAutostartRegistry"/> in-memory fake ile doğrulanır.
/// </summary>
public class AutostartServiceTests
{
    private sealed class FakeRegistry : IAutostartRegistry
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        public void Set(string name, string command) => _values[name] = command;
        public void Remove(string name) => _values.Remove(name);
        public bool Exists(string name) => _values.ContainsKey(name);
        public string? CommandFor(string name) => _values.TryGetValue(name, out var v) ? v : null;
    }

    [Fact]
    public void Apply_enabled_writes_the_run_value_with_the_injected_command()
    {
        var reg = new FakeRegistry();
        var svc = new AutostartService(reg, "BuildOrchestrator", @"C:\app\BuildOrchestrator.App.exe --autostart");

        svc.Apply(autostartEnabled: true);

        Assert.True(reg.Exists("BuildOrchestrator"));
        Assert.Equal(@"C:\app\BuildOrchestrator.App.exe --autostart", reg.CommandFor("BuildOrchestrator"));
    }

    [Fact]
    public void Apply_disabled_removes_the_run_value()
    {
        var reg = new FakeRegistry();
        var svc = new AutostartService(reg, "BuildOrchestrator", "cmd");
        svc.Apply(autostartEnabled: true);

        svc.Apply(autostartEnabled: false);

        Assert.False(reg.Exists("BuildOrchestrator"));
    }

    [Fact]
    public void Apply_is_idempotent_for_repeated_enable_and_disable()
    {
        var reg = new FakeRegistry();
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
}
