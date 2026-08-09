using System;
using System.Collections.Generic;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.State;
using Xunit;

namespace BuildOrchestrator.Tests.State;

/// <summary>
/// [Task 7] <see cref="BuildStateStore.IsCycleNonConvergent"/>: bir SCC üyesinin, PLANLANAN (şu anki) bileşik
/// imzada daha önce yakınsamadığı hafızası — <see cref="BuildState.NonConvergentSignature"/> alanının TEK
/// okuyucusu. Saf predikat: gerçek dosya I/O'su yok, doğrudan bellekteki map üzerinden test edilir (dosya
/// tabanlı davranış zaten <see cref="BuildStateStoreTests"/>'te).
/// </summary>
public class CycleConvergenceMemoryTests
{
    private const string ProjectId = @"C:\repo\A\A.csproj";

    private static IReadOnlyDictionary<string, BuildState> MapOf(BuildState state) =>
        new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase) { [state.ProjectId] = state };

    [Fact] // hiç kayıt yok ⇒ hafıza yok
    public void No_record_is_not_remembered_as_non_convergent()
    {
        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase);
        Assert.False(BuildStateStore.IsCycleNonConvergent(state, ProjectId, "sig"));
    }

    [Fact] // kayıt var ama NonConvergentSignature hiç yazılmamış (sıradan bir kayıt) ⇒ hafıza yok
    public void A_record_without_a_non_convergent_signature_is_not_remembered()
    {
        var state = MapOf(new BuildState(ProjectId, "sig", LastResult: BuildResult.Succeeded));
        Assert.False(BuildStateStore.IsCycleNonConvergent(state, ProjectId, "sig"));
    }

    [Fact] // kayıtlı NonConvergentSignature ŞU ANKİ imzayla eşleşiyor ⇒ hafıza VAR
    public void A_record_whose_non_convergent_signature_matches_the_current_one_is_remembered()
    {
        var state = MapOf(new BuildState(ProjectId, BuiltSignature: null, LastResult: BuildResult.Failed,
            NonConvergentSignature: "sig"));
        Assert.True(BuildStateStore.IsCycleNonConvergent(state, ProjectId, "sig"));
    }

    [Fact] // kaynak değişti: kayıtlı imza ile ŞU ANKİ imza FARKLI ⇒ hafıza artık geçersiz
    public void A_record_whose_non_convergent_signature_differs_from_the_current_one_is_not_remembered()
    {
        var state = MapOf(new BuildState(ProjectId, BuiltSignature: null, LastResult: BuildResult.Failed,
            NonConvergentSignature: "old-sig"));
        Assert.False(BuildStateStore.IsCycleNonConvergent(state, ProjectId, "new-sig"));
    }

    [Fact] // currentSignature null (hollow / imza hesaplanamadı) ⇒ karşılaştırma tabanı yok, her zaman false
    public void A_null_current_signature_is_never_remembered_even_with_a_matching_record()
    {
        var state = MapOf(new BuildState(ProjectId, BuiltSignature: null, LastResult: BuildResult.Failed,
            NonConvergentSignature: "sig"));
        Assert.False(BuildStateStore.IsCycleNonConvergent(state, ProjectId, null));
    }

    [Fact] // map'in kendisi null (ör. stateStore hiç Load edilmemiş) ⇒ ASLA fırlamaz, false döner
    public void A_null_state_map_is_never_remembered_and_never_throws()
    {
        Assert.False(BuildStateStore.IsCycleNonConvergent(null, ProjectId, "sig"));
    }
}
