namespace OrandOverlay;

// Display history only. Never supplies a current round to recognition or gameplay.
internal sealed class DiagnosticRoundPresentation
{
    private string? _binding;
    private int? _lastConfirmedRound;

    internal string Update(IDiagnosticInventoryReference? value, bool current)
    {
        if (!current || value is not (DiagnosticInventoryObservation or DiagnosticBasicInventoryObservation) ||
            value.Availability != DiagnosticInventoryAvailability.Ready)
        {
            _binding = null;
            _lastConfirmedRound = null;
            return "라운드 확인 중";
        }
        var binding = (value.BindingContextId.Length > 0 ? value.BindingContextId : value.ContextId) + ":" + value.ViewSlot;
        if (_binding != binding) _lastConfirmedRound = null;
        _binding = binding;
        if (value.GrowthAttributionAvailable) _lastConfirmedRound = value.ObservedRound;
        return _lastConfirmedRound is { } round ? $"최근 확인 {round}라운드" : "라운드 확인 중";
    }
}
