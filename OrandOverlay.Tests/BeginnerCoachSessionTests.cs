using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BeginnerCoachSessionTests
{
    [Fact]
    public void AcquisitionAfterTransientReadConfirmsPriorActionAndNewMatchClearsIt()
    {
        var session = new BeginnerCoachSession(new DataCatalog());
        var initial = BeginnerCoachPlannerTests.ReadyFrame();
        Assert.Equal(CoachActionKind.Craft, session.Update(initial).Kind);
        session.Update(initial with { Revision = 2, IsCurrent = false });
        var acquired = initial with
        {
            Revision = 3, CraftSteps = [],
            Inventory = ImmutableDictionary<string, int>.Empty.Add("goal", 1)
        };
        var result = session.Update(acquired);
        Assert.NotEmpty(result.CompletionNotice);
        Assert.Same(result, session.Update(initial));
        var reset = session.Update(initial with { MatchGeneration = 2, Revision = 1 });
        Assert.Empty(reset.CompletionNotice);
    }
}
