using OrandOverlay;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BoardSelectionTests
{
    [Fact]
    public void VisibleClusterChildWinsWhenMainRecommendationSharesRouteId()
    {
        var main = Recommendation("craft:rawcode:W30h", owned: 2, required: 8);
        var child = Recommendation("craft:rawcode:W30h", owned: 8, required: 9);

        var selected = BoardSelection.Resolve([main], [child], child.Route.Id);

        Assert.Same(child, selected);
        Assert.Equal(1, Assert.Single(
            RecommendationPresentation.BoardMissingLeaves(
                selected!.RecipeProgress, preferCommons: true)).MissingCount);
    }

    [Fact]
    public void WpfBoardShowsSelectedChildPercentAndItsMissingCommonCount()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var main = Recommendation("craft:rawcode:W30h", owned: 2, required: 8);
                var child = Recommendation("craft:rawcode:W30h", owned: 8, required: 9);
                var now = new StackPanel();
                var flow = new StackPanel();
                var board = new StackPanel();

                RecommendationBoard.Fill(
                    now, flow, board, [main], [], child.Route.Id, _ => { },
                    selectedChildren: [child], clusterHeadId: main.Route.Id);

                Assert.Contains("89%", Texts(now));
                Assert.DoesNotContain("25%", Texts(now));
                Assert.Contains("부족 ×1", Texts(board));
                Assert.DoesNotContain("부족 ×6", Texts(board));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IReadOnlyList<string> Texts(DependencyObject root)
    {
        var result = new List<string>();
        Walk(root);
        return result;

        void Walk(DependencyObject node)
        {
            if (node is TextBlock text) result.Add(text.Text);
            for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
                 index++)
                Walk(System.Windows.Media.VisualTreeHelper.GetChild(node, index));
        }
    }

    private static Recommendation Recommendation(string routeId, long owned, long required) =>
        new()
        {
            Route = new RouteDefinition
            {
                Id = routeId,
                GoalUnitId = "rawcode:W30h",
                Name = "베르고 히든"
            },
            RecipeProgress = new RecipeProgress
            {
                OwnedLeafCount = owned,
                RequiredLeafCount = required,
                Leaves =
                [
                    new RecipeLeafProgress
                    {
                        UnitId = "rawcode:H00h",
                        Name = "루피",
                        Tier = "흔함",
                        OwnedCount = owned,
                        RequiredCount = required
                    }
                ]
            },
            CompositionUnits =
            [
                new CompositionUnitDetail
                {
                    UnitId = "rawcode:W30h",
                    Name = "베르고",
                    Tier = "히든"
                }
            ]
        };
}
