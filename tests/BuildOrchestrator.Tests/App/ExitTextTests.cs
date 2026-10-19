using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Exit] Onay kartının ve kapanış ekranının metinleri (<see cref="ExitText"/>) — tasarımın verdiği cümleler birebir
/// ("Exit stops the build, then quits.", "Nothing is running. Exit quits right away.", "Ending 5 processes…"), tasarımın
/// örneklemediği işlerin cümleleri aynı kalıpla. Git yazımı (branch switch, pull) beklenir; cümlesi ve düğmesi bunu söyler.
/// </summary>
public sealed class ExitTextTests
{
    [Fact]
    public void A_build_card_reads_as_the_design_draws_it()
    {
        Assert.Equal("Build in progress", ExitText.Heading(ExitWork.Build));
        Assert.Equal("Exit stops the build, then quits.", ExitText.Sentence(ExitWork.Build));
        Assert.Equal(
            "4 projects in flight stay unbuilt. Finished projects keep their results; the next Build picks up the rest.",
            ExitText.Outcome(ExitWork.Build, projectsInFlight: 4));
        Assert.Equal("Stop and exit", ExitText.Action(ExitWork.Build));
        Assert.Equal("Cancel", ExitText.Cancel);
        Assert.Equal("Ends on exit", ExitText.EndsOnExit);
        Assert.Equal("×4", ExitText.ProcessCount(4));
    }

    [Fact]
    public void One_project_in_flight_is_singular()
    {
        Assert.StartsWith("1 project in flight stays unbuilt. ", ExitText.Outcome(ExitWork.Rebuild, projectsInFlight: 1),
            StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_project_in_flight_the_outcome_keeps_only_the_results_sentence()
    {
        Assert.Equal("Finished projects keep their results; the next Build picks up the rest.",
            ExitText.Outcome(ExitWork.Resolve, projectsInFlight: 0));
    }

    [Fact]
    public void With_nothing_running_the_card_says_exit_quits_right_away()
    {
        Assert.Equal("Exit", ExitText.Heading(ExitWork.None));
        Assert.Equal("Nothing is running. Exit quits right away.", ExitText.Sentence(ExitWork.None));
        Assert.Null(ExitText.Outcome(ExitWork.None, projectsInFlight: 0));
        Assert.Equal("Exit", ExitText.Action(ExitWork.None));
    }

    [Theory]
    [InlineData(ExitWork.BranchSwitch, "Exit waits for the branch switch, then quits.")]
    [InlineData(ExitWork.Pull, "Exit waits for the pull, then quits.")]
    public void A_git_write_is_waited_for_and_the_card_says_so(ExitWork work, string sentence)
    {
        Assert.True(work.Waits());
        Assert.Equal(sentence, ExitText.Sentence(work));
        Assert.Equal("Wait and exit", ExitText.Action(work));
        Assert.StartsWith("Finishing ", ExitText.StoppingStep(work), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExitWork.Build, "Build in progress", "Exit stops the build, then quits.")]
    [InlineData(ExitWork.Rebuild, "Rebuild in progress", "Exit stops the rebuild, then quits.")]
    [InlineData(ExitWork.Resolve, "Resolve in progress", "Exit stops the resolve, then quits.")]
    [InlineData(ExitWork.Clean, "Clean in progress", "Exit stops the clean, then quits.")]
    [InlineData(ExitWork.DeepClean, "Deep clean in progress", "Exit stops the deep clean, then quits.")]
    [InlineData(ExitWork.Optimize, "Optimize in progress", "Exit stops the optimize, then quits.")]
    [InlineData(ExitWork.Sync, "Sync in progress", "Exit stops the sync, then quits.")]
    public void Work_that_is_cut_off_is_stopped(ExitWork work, string heading, string sentence)
    {
        Assert.False(work.Waits());
        Assert.Equal(heading, ExitText.Heading(work));
        Assert.Equal(sentence, ExitText.Sentence(work));
        Assert.Equal("Stop and exit", ExitText.Action(work));
        Assert.NotNull(ExitText.Outcome(work, projectsInFlight: 0));
    }

    [Fact]
    public void The_closing_screen_names_the_app_and_the_work_with_its_progress()
    {
        Assert.Equal("Closing " + AppIdentity.Product, ExitText.ClosingHeading);
        Assert.Equal("Build · 14/38", ExitText.ClosingContext(ExitWork.Build, "14/38"));
        Assert.Equal("Sync", ExitText.ClosingContext(ExitWork.Sync, progress: null));
    }

    [Fact]
    public void The_closing_steps_read_as_the_design_draws_them()
    {
        Assert.Equal("Stopping build…", ExitText.StoppingStep(ExitWork.Build));
        Assert.Equal("Stopping deep clean…", ExitText.StoppingStep(ExitWork.DeepClean));
        Assert.Equal("Ending 5 processes…", ExitText.EndingStep(5));
        Assert.Equal("Ending 1 process…", ExitText.EndingStep(1));
        Assert.Equal("Quitting…", ExitText.EndingStep(0));
    }
}
