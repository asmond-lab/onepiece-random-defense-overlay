namespace OrandOverlay;

internal sealed partial class OverlayExecutionContext
{
    private readonly bool _syntheticGoroseiAllowed;

    // Explicit code-only composition root; settings and ordinary Fixture cannot opt in.
    internal static OverlayExecutionContext SyntheticGoroseiFixture(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new(false, null, Clone(settings), _ => { }, syntheticGorosei: true);
    }

    internal SyntheticGoroseiInput CreateSyntheticGorosei(GoroseiMode mode) => SyntheticGoroseiInput.Create(this, mode);

    internal sealed class SyntheticGoroseiInput
    {
        private readonly OverlayExecutionContext _issuer;
        private SyntheticGoroseiInput(OverlayExecutionContext issuer, GoroseiMode mode)
        {
            _issuer = issuer;
            Marker = new(GoroseiMarkerStatus.SelectedIdentity, mode,
                "Synthetic Fixture identity/effect model; not native/game verification", []);
        }
        internal GoroseiMarkerSnapshot Marker { get; }
        internal static SyntheticGoroseiInput Create(OverlayExecutionContext issuer, GoroseiMode mode)
        {
            if (!issuer._syntheticGoroseiAllowed || issuer.RuntimeEnabled)
                throw new InvalidOperationException("Explicit synthetic Fixture context required.");
            if (mode is not (GoroseiMode.Saturn or GoroseiMode.Warcury or GoroseiMode.Nasjuro))
                throw new ArgumentOutOfRangeException(nameof(mode));
            return new(issuer, mode);
        }
        internal bool Matches(OverlayExecutionContext context, GoroseiMarkerSnapshot marker) =>
            ReferenceEquals(_issuer, context) && context._syntheticGoroseiAllowed && !context.RuntimeEnabled &&
            ReferenceEquals(Marker, marker) && marker.Status == GoroseiMarkerStatus.SelectedIdentity;
    }
}
