using BuildOrchestrator.Core.State;
using Xunit;

namespace BuildOrchestrator.Tests.State;

/// <summary>
/// [spec 2026-09-18 §1-14/Task 2] <see cref="FailureClassification.IsCompilerFailure"/>: saf sınıflandırıcı,
/// I/O yok. Yalnız derleyicinin/MSBuild'in kendi sıfır-dışı çıkışı (<c>"exit N"</c>) kanıt sayılır — timeout,
/// durdurma, invoke hatası ve başarı (reason yok) KANIT DEĞİLDİR.
/// </summary>
public class FailureClassificationTests
{
    [Theory]
    [InlineData("exit 1", true)]
    [InlineData("exit 0", true)]     // biçim yeter — çağıran bunu yalnız sonuç Failed'ken üretir
    [InlineData("exit -1", true)]
    [InlineData("timeout", false)]
    [InlineData("stopped", false)]
    [InlineData("invoke error: boom", false)]
    [InlineData("exitcode 1", false)]  // önekle BAŞLAMIYOR — "exit " boşluğu dahil tam eşleşme gerekir
    [InlineData("", false)]
    public void Classifies_the_reason_string(string reason, bool expected) =>
        Assert.Equal(expected, FailureClassification.IsCompilerFailure(reason));

    [Fact]
    public void A_null_reason_is_not_evidence() =>
        Assert.False(FailureClassification.IsCompilerFailure(null));
}
