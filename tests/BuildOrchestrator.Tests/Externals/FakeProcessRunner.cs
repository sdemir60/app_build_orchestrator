using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Harici araç yüzeylerini (tf.exe, vswhere) gerçek process açmadan sınayan çalıştırıcı: her çağrı için
/// verilen cevabı döner ve gördüğü tüm spec'leri kaydeder — argüman sözleşmesi de böyle pinlenir.
/// </summary>
internal sealed class FakeProcessRunner(params ProcessResult[] responses) : IProcessRunner
{
    private int _next;

    public List<ProcessSpec> Calls { get; } = [];

    public ProcessSpec LastSpec => Calls[^1];

    /// <summary>Belirtilen argümanı içeren ilk çağrının spec'i (birden çok komut çalıştıran akışlar için).</summary>
    public ProcessSpec CallContaining(string argument)
        => Calls.Find(c => c.Arguments.Contains(argument))
           ?? throw new InvalidOperationException($"'{argument}' argümanını taşıyan bir çağrı yok.");

    public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
    {
        Calls.Add(spec);
        var response = responses[Math.Min(_next, responses.Length - 1)];
        _next++;
        return Task.FromResult(response);
    }

    public static ProcessResult Output(string stdout) => new(0, stdout, "", TimeSpan.Zero, TimedOut: false);

    public static ProcessResult Failure(int exitCode, string stderr) => new(exitCode, "", stderr, TimeSpan.Zero, TimedOut: false);
}
