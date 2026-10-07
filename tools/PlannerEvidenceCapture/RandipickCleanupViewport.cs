namespace PlannerEvidenceCapture;

// Fixed logical capture sizes, independent of the monitor; product UiScale is unchanged.
public sealed record RandipickCleanupViewport(double Width, double Height, double ContentScale)
{
    public static RandipickCleanupViewport Resolve(string surface, double loadedWidth, double loadedHeight,
        double loadedScale, int mainWidth = 1080) => surface == "overlay"
        ? new(540, 740, 1)
        : new(mainWidth, mainWidth == 1080 ? 720 : 620, 1);
}
