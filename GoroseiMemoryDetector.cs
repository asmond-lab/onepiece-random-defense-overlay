namespace OrandOverlay;

internal static class GoroseiMemoryDetector
{
    // ORDR_S2_2.314[R] war3map.j::ZbN creates exactly one hidden Player(7)
    // marker: E20o=Warcury, 130o=Saturn, 230o=Nasjuro. vTE later reads the
    // same unit type to apply the global effect, so this is the map's authority.
    internal static GoroseiMode FromRawcode(uint rawcode) =>
        RawcodeCodec.Format(rawcode) switch
        {
            "E20o" => GoroseiMode.Warcury,
            "130o" => GoroseiMode.Saturn,
            "230o" => GoroseiMode.Nasjuro,
            _ => GoroseiMode.None
        };

    internal static GoroseiMode Resolve(GoroseiMode current,
        GoroseiMode detected) =>
        detected == GoroseiMode.None ? current : detected;
}
