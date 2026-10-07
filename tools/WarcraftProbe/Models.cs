namespace WarcraftProbe;

public sealed record SectionInfo(string Name, uint Rva, uint VirtualSize, uint RawOffset, uint RawSize, uint Characteristics, string Sha256);
public sealed record AnchorInfo(string Kind, string Name, uint[] Rvas, string Status);
public sealed record ImageInfo(string FileName, long FileSize, string Sha256, string? FileVersion, ushort Machine,
    ulong PreferredBase, uint ImageSize, uint HeaderSize, uint EntryPointRva, uint TimeDateStamp,
    SectionInfo[] Sections, AnchorInfo[] Anchors);
public sealed record RegionInfo(uint Rva, ulong Size, uint State, uint Protection, uint Type);
public sealed record CodeCheck(string Name, uint Rva, uint Size, string Status, string? Sha256);
public sealed record RawcodeCount(string Rawcode, int Count);
public sealed record StructureInfo(string ProfileId, string Status, double ElapsedMilliseconds,
    int? CurrentViewSlot, int? ObservedObjectCount, int? CurrentViewObjectCount, int? OtherViewObjectCount,
    RawcodeCount[] Rawcodes, string? WorldToken, string? UiToken, string? VmToken, string? Reason)
{
    public bool LocalIdentityVerified => false;
    public bool AliveVerified => false;
    public bool CompleteGameplayInventoryVerified => false;
    public bool CraftabilityVerified => false;
    public bool GameplayReady => false;
    public string Scope => "Current-view allocated objects only; not qualified local/alive inventory";
}
public sealed record LiveInfo(int ProcessId, DateTimeOffset ProcessStartedAt, uint ModuleSize,
    bool HeaderBytesEqual, bool ResourceBytesEqual, string BindingScope, RegionInfo[] Regions,
    CodeCheck[] CodeChecks, StructureInfo Structures, long RequestedBytes, int QueryCalls,
    double ElapsedMilliseconds, string Status)
{
    public bool AtomicSnapshot => false;
    public bool RuntimeWholeImageHashVerified => false;
}
public sealed record Snapshot(int SchemaVersion, string ToolVersion, string CaptureId, string Mode, string Label,
    DateTimeOffset StartedAt, DateTimeOffset FinishedAt, ImageInfo Image, LiveInfo? Live)
{
    public bool GameplayReady => false;
    public bool AutomaticProfileApproval => false;
    public string SafetyScope => "Read/query only; bounded whitelisted data; no full process dump, game input, target execution or protection changes";
}
public sealed record Difference(string Area, string Name, string Before, string After, string Meaning);
public sealed record Comparison(int SchemaVersion, string BeforeCaptureId, string AfterCaptureId,
    string BeforeVersion, string AfterVersion, bool SameImage, bool SameProcessEpoch, Difference[] Differences,
    string[] Limitations)
{
    public bool AutomaticProfileApproval => false;
}
