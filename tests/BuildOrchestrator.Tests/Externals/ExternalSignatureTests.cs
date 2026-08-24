using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Externals;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [D4] Harici projenin imzası: configuration + VCS türü + revizyon kimliği. Dirty bir çalışma kopyası
/// build'i zaten iptal ettiği için revizyon kimliği tam imzadır — dosya içeriği taranmaz.
/// </summary>
public class ExternalSignatureTests
{
    [Fact]
    public void Signature_is_deterministic_for_the_same_terms()
    {
        string first = ExternalSignature.Compute("Debug", VcsKind.Git, "abc123");
        string second = ExternalSignature.Compute("Debug", VcsKind.Git, "abc123");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length); // SHA256 hex
    }

    [Fact]
    public void Configuration_changes_the_signature()
        => Assert.NotEqual(
            ExternalSignature.Compute("Debug", VcsKind.Git, "abc123"),
            ExternalSignature.Compute("Release", VcsKind.Git, "abc123"));

    [Fact]
    public void Vcs_kind_changes_the_signature()
        => Assert.NotEqual(
            ExternalSignature.Compute("Debug", VcsKind.Git, "abc123"),
            ExternalSignature.Compute("Debug", VcsKind.Tfvc, "abc123"));

    [Fact]
    public void Revision_changes_the_signature()
        => Assert.NotEqual(
            ExternalSignature.Compute("Debug", VcsKind.Git, "abc123"),
            ExternalSignature.Compute("Debug", VcsKind.Git, "def456"));

    [Fact]
    public void An_unknown_revision_is_not_the_same_as_an_empty_one()
    {
        // Bilinmeyen revizyon ile boş revizyon aynı imzaya düşerse, revizyonu okunamayan bir çalışma
        // kopyası bir sonraki koşuda "güncel" görünürdü.
        Assert.NotEqual(
            ExternalSignature.Compute("Debug", VcsKind.Git, null),
            ExternalSignature.Compute("Debug", VcsKind.Git, ""));
    }
}
