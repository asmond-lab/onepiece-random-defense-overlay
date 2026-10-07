using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

public sealed record MapArchivePin(
    string FileName,
    long LengthBytes,
    string Sha256);

public sealed record SourceMemberPin(
    string Name,
    long LengthBytes,
    string Sha256);

public sealed record SourcePin(
    string Artifact,
    int StartLine,
    int EndLine,
    string Purpose);

public sealed record W3uParserLimits(
    long MaxMemberBytes,
    int MaxTables,
    int MaxRecordsPerTable,
    int MaxModificationsPerRecord,
    int MaxStringBytes);

public sealed record W3uTableEvidence(
    string Name,
    long CountOffset,
    int RecordCount,
    long RecordsStartOffset,
    long EndOffset);

public sealed record W3uLayoutEvidence(
    int Version,
    long HeaderEndOffset,
    ImmutableArray<W3uTableEvidence> Tables,
    int TotalRecordCount,
    int TotalModificationCount,
    int ZeroSanityMarkerCount,
    long ConsumedBytes,
    long TrailingBytes);

public sealed record W3uFieldEvidence(
    int ModificationIndex,
    long StartOffset,
    string FieldId,
    int ValueType,
    long ValueOffset,
    string Value,
    long SanityOffset,
    string SanityValue,
    long EndOffset);

public sealed record W3uRecordEvidence(
    string ObjectId,
    string BaseId,
    string Table,
    int RecordIndex,
    long StartOffset,
    long EndOffset,
    int Reserved0,
    int Reserved1,
    long ModificationCountOffset,
    int ModificationCount,
    string SemanticKind,
    ImmutableArray<W3uFieldEvidence> Fields);

public sealed record W3uEvidence(
    string MemberName,
    W3uParserLimits ParserLimits,
    W3uLayoutEvidence Layout,
    ImmutableArray<W3uRecordEvidence> Records);

public sealed record WtsRecordEvidence(
    string ObjectId,
    string FieldId,
    int RecordId,
    long RecordStartOffset,
    long RecordEndOffset,
    long TextStartOffset,
    long TextEndOffset,
    string ResolvedText,
    string RecordSha256);

public sealed record WtsEvidence(
    string MemberName,
    string TargetTextEncoding,
    int ParsedRecordCount,
    ImmutableArray<WtsRecordEvidence> Records);

public sealed record MapSourceMetadata(
    string MapVersion,
    MapArchivePin Archive,
    ImmutableArray<SourceMemberPin> Members,
    ImmutableArray<SourcePin> JassPins,
    W3uEvidence W3uEvidence,
    WtsEvidence WtsEvidence)
{
    [JsonIgnore]
    public SourceMemberPin Jass => Member("war3map.j");

    [JsonIgnore]
    public SourceMemberPin W3u => Member("war3map.w3u");

    [JsonIgnore]
    public SourceMemberPin Wts => Member("war3map.wts");

    [JsonIgnore]
    public string JassSha256 => Jass.Sha256;

    [JsonIgnore]
    public long JassLengthBytes => Jass.LengthBytes;

    private SourceMemberPin Member(string name) =>
        Members.Single(member => member.Name == name);
}

public sealed record JassSourcePin(
    string Function,
    int StartLine,
    int EndLine,
    int EvidenceLine);

public sealed record StoryRewardComponent(
    string Kind,
    string Id,
    long Amount,
    int Limit,
    int ChanceNumerator,
    int ChanceDenominator,
    string Condition,
    ImmutableArray<JassSourcePin> Sources);

public sealed record RewardComponentGroups(
    ImmutableArray<StoryRewardComponent> EveryPlayerBase,
    ImmutableArray<StoryRewardComponent> ContributionAtLeast25Percent,
    [property: JsonPropertyName("MVP")] ImmutableArray<StoryRewardComponent> Mvp,
    ImmutableArray<StoryRewardComponent> HiddenOrSideEffect);

public sealed record RewardSourcePin(
    int StartLine,
    int EndLine,
    string Function,
    string Rawcode,
    int Count);

public sealed record StoryStage(
    int Ordinal,
    string ObjectiveRawcode,
    int OwnerId,
    string MilestoneId,
    ImmutableArray<string> RewardKinds,
    RewardComponentGroups RewardComponents)
{
    private static readonly ImmutableHashSet<string> RewardRawcodes =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            "e016", "e017", "e018", "e019", "e01A", "e0IX");

    [JsonIgnore]
    public ImmutableArray<RewardSourcePin> RewardSources =>
        RewardComponents.EveryPlayerBase
            .Where(component => component.Kind == "Unit" &&
                RewardRawcodes.Contains(component.Id))
            .Select(component => new RewardSourcePin(
                component.Sources[0].StartLine,
                component.Sources[0].EndLine,
                component.Sources[0].Function,
                component.Id,
                checked((int)component.Amount)))
            .ToImmutableArray();
}

public sealed record StorySourceReference(
    string MapVersion,
    string SourceMetadataSha256,
    string ArchiveSha256,
    string JassSha256,
    string W3uSha256,
    string WtsSha256);

internal sealed record StoryProfileDocument(
    StorySourceReference Source,
    ImmutableArray<StoryStage> Stages);

public sealed record StoryProgressionProfile(
    MapSourceMetadata Source,
    ImmutableArray<StoryStage> Stages)
{
    public StoryStage ResolveActiveStage(string objectiveRawcode, int ownerId) =>
        Stages.Single(stage => stage.ObjectiveRawcode == objectiveRawcode &&
            stage.OwnerId == ownerId);
}

public static class MapStoryProfileLoader
{
    private const string SourceFileName = "map-source-metadata-2314.json";
    private const string StoryFileName = "story-progression-2314.json";
    private const string ExpectedSourceMetadataSha256 =
        "2d262ff929fcae3d70a94c6c76969708f37f28a62b9dd06b52b0af80d4c10c82";
    private const string ExpectedStoryProfileSha256 =
        "8c24aff6b74ebc6a4117c9aa57bfc236cbd286d12ad85faf802c6453dc58ae0b";
    private const string ExpectedSourceContractSha256 =
        "bba2a20b78d5af531c8289ae35683f4ea33928048b43461009b4a4e0967d04cc";
    private const string ExpectedStoryContractSha256 =
        "ed1e0e6c15c57c4887b275abdd8e70cb8e9bcaa77157c5e58c3004916bf9c596";

    private static readonly string[] ExpectedObjectiveRawcodes =
    [
        "n000", "n002", "n003", "n004", "n005", "n006", "n007",
        "n008", "n00A", "n001", "n00C", "n00D", "n00B", "n009"
    ];

    private static readonly string[] ExpectedMilestones =
    [
        "stage1", "stage2", "stage3", "stage4", "stage5", "stage6",
        "stage7", "stage8", "Marineford", "stage10", "stage11",
        "stage12", "stage13", "stage14"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64
    };

    private static readonly JsonSerializerOptions ContractJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    public static StoryProgressionProfile LoadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        try
        {
            var sourceBytes = ReadVerifiedBytes(
                Path.Combine(directory, SourceFileName),
                ExpectedSourceMetadataSha256);
            var storyBytes = ReadVerifiedBytes(
                Path.Combine(directory, StoryFileName),
                ExpectedStoryProfileSha256);
            var source = Deserialize<MapSourceMetadata>(sourceBytes, SourceFileName);
            var story = Deserialize<StoryProfileDocument>(storyBytes, StoryFileName);
            Validate(source, story);
            return new(source, story.Stages);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or JsonException or NotSupportedException or
            OverflowException or ArgumentException)
        {
            throw new InvalidDataException("Story profile could not be loaded safely.",
                exception);
        }
    }

    internal static void Validate(
        MapSourceMetadata source,
        StoryProfileDocument story)
    {
        ValidateSource(source);
        ValidateStory(source, story);
    }

    internal static string ComputeContractSha256<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(value, ContractJsonOptions)))
            .ToLowerInvariant();

    private static byte[] ReadVerifiedBytes(string path, string expectedSha256)
    {
        var bytes = CanonicalizeLineEndings(File.ReadAllBytes(path));
        var actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unapproved story data bytes: {path}");
        return bytes;
    }

    private static byte[] CanonicalizeLineEndings(byte[] bytes)
    {
        var firstCarriageReturn = Array.IndexOf(bytes, (byte)'\r');
        if (firstCarriageReturn < 0)
            return bytes;

        var canonical = new byte[bytes.Length];
        Buffer.BlockCopy(bytes, 0, canonical, 0, firstCarriageReturn);
        var written = firstCarriageReturn;
        for (var index = firstCarriageReturn; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\r')
            {
                canonical[written++] = (byte)'\n';
                if (index + 1 < bytes.Length && bytes[index + 1] == (byte)'\n')
                    index++;
            }
            else
            {
                canonical[written++] = bytes[index];
            }
        }

        return canonical[..written];
    }

    private static T Deserialize<T>(byte[] bytes, string fileName) =>
        JsonSerializer.Deserialize<T>(bytes, JsonOptions)
        ?? throw new InvalidDataException($"Story data is empty: {fileName}");

    private static void ValidateSource(MapSourceMetadata source)
    {
        if (source.MapVersion != "2.314" ||
            source.Archive != new MapArchivePin(
                "ORDR_S2_2.314[R].w3x",
                134_758_580,
                "f9ddd3af7c0fbfd39b7a6df2ca0f5f295675bba5e8b91a5322dcec93d7ca8a83") ||
            source.Members.IsDefault || source.Members.Length != 3 ||
            source.JassPins.IsDefault || source.JassPins.IsEmpty ||
            source.W3uEvidence is null || source.WtsEvidence is null)
            throw new InvalidDataException("Unverified map source contract.");

        var expectedMembers = new[]
        {
            new SourceMemberPin("war3map.j", 3_069_240,
                "0bccc47907a9505f38efaf6bbf20228a728eabdfaec3209cca7df2269bfc2028"),
            new SourceMemberPin("war3map.w3u", 857_425,
                "a9aa2cb9c08130c3bee970aecb05b62fdc3db867685f4997dd2533735d2ea278"),
            new SourceMemberPin("war3map.wts", 1_756_386,
                "bef1477f56bfffef3ef6b030f324b7fc75a69564760e185c8aa4da8a618a275c")
        };
        if (!source.Members.SequenceEqual(expectedMembers))
            throw new InvalidDataException("Map member pins do not match the authority.");

        ValidateW3u(source.W3uEvidence, source.W3u);
        ValidateWts(source.WtsEvidence, source.Wts);
        ValidateSemanticDistinction(source);

        var contractHash = ComputeContractSha256(source);
        if (!contractHash.Equals(ExpectedSourceContractSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Source semantic contract mismatch: {contractHash}");
    }

    private static void ValidateW3u(W3uEvidence evidence, SourceMemberPin member)
    {
        var layout = evidence.Layout;
        if (evidence.MemberName != member.Name ||
            evidence.ParserLimits != new W3uParserLimits(
                536_870_912, 2, 100_000, 100_000, 1_048_576) ||
            layout.Version != 3 || layout.HeaderEndOffset != 4 ||
            layout.Tables.IsDefault || !layout.Tables.SequenceEqual(new[]
            {
                new W3uTableEvidence("Original", 4, 1, 8, 93),
                new W3uTableEvidence("Custom", 93, 1_410, 97, 857_425)
            }) ||
            layout.TotalRecordCount != 1_411 ||
            layout.TotalModificationCount != 45_163 ||
            layout.ZeroSanityMarkerCount != 45_163 ||
            layout.ConsumedBytes != member.LengthBytes ||
            layout.TrailingBytes != 0 ||
            evidence.Records.IsDefault || evidence.Records.Length != 7 ||
            !evidence.Records.Select(record => record.ObjectId).SequenceEqual(
                new[] { "e0IX", "e0IA", "e01A", "e019", "e018", "e017", "e016" }))
            throw new InvalidDataException("W3U layout evidence is incomplete.");

        foreach (var record in evidence.Records)
        {
            if (record.BaseId != "ewsp" || record.Table != "Custom" ||
                record.StartOffset < 97 || record.EndOffset > layout.ConsumedBytes ||
                record.StartOffset >= record.EndOffset ||
                record.ModificationCountOffset < record.StartOffset ||
                record.ModificationCount <= 0 ||
                record.ModificationCount > evidence.ParserLimits.MaxModificationsPerRecord ||
                record.Reserved0 != 1 || record.Reserved1 != 0 ||
                record.Fields.IsDefault || record.Fields.IsEmpty ||
                record.Fields.Any(field =>
                    field.ModificationIndex < 0 ||
                    field.ModificationIndex >= record.ModificationCount ||
                    field.StartOffset < record.StartOffset ||
                    field.EndOffset > record.EndOffset ||
                    field.StartOffset >= field.EndOffset ||
                    field.ValueType is < 0 or > 3 ||
                    field.SanityValue != "00000000" ||
                    field.SanityOffset + 4 != field.EndOffset))
                throw new InvalidDataException("W3U record evidence is invalid.");
        }
    }

    private static void ValidateWts(WtsEvidence evidence, SourceMemberPin member)
    {
        if (evidence.MemberName != member.Name ||
            evidence.TargetTextEncoding != "UTF-8" ||
            evidence.ParsedRecordCount != 10_646 ||
            evidence.Records.IsDefault || evidence.Records.Length != 14 ||
            evidence.Records.Any(record =>
                record.RecordStartOffset < 0 ||
                record.RecordEndOffset > member.LengthBytes ||
                record.RecordStartOffset >= record.TextStartOffset ||
                record.TextStartOffset >= record.TextEndOffset ||
                record.TextEndOffset >= record.RecordEndOffset ||
                record.RecordSha256.Length != 64 ||
                string.IsNullOrEmpty(record.ResolvedText)))
            throw new InvalidDataException("WTS evidence is incomplete.");
    }

    private static void ValidateSemanticDistinction(MapSourceMetadata source)
    {
        var transcendence = source.W3uEvidence.Records.SingleOrDefault(record =>
            record.ObjectId == "e01A");
        var dummy = source.W3uEvidence.Records.SingleOrDefault(record =>
            record.ObjectId == "e0IA");
        if (transcendence is null || dummy is null ||
            transcendence.RecordIndex != 1_288 ||
            transcendence.StartOffset != 768_517 ||
            transcendence.EndOffset != 768_786 ||
            transcendence.SemanticKind != "TranscendenceReward" ||
            !HasField(transcendence, "uabi", 3, "Aeth,Avul") ||
            !HasField(transcendence, "uspe", 0, "1") ||
            !HasField(transcendence, "unam", 3, "TRIGSTR_2118") ||
            dummy.RecordIndex != 676 || dummy.StartOffset != 455_895 ||
            dummy.EndOffset != 456_397 ||
            dummy.SemanticKind != "MechanicalDummy" ||
            !HasField(dummy, "umdl", 3, "bijuuexplosion.mdl") ||
            !HasField(dummy, "uhpm", 0, "3") ||
            !HasField(dummy, "utyp", 3, "mechanical") ||
            !HasField(dummy, "unam", 3, "TRIGSTR_894"))
            throw new InvalidDataException(
                "e01A transcendence and e0IA dummy evidence is not authoritative.");

        if (!HasText(source, "e01A", "unam", 2_118, "초월위습") ||
            !HasText(source, "e01A", "utip", 2_119, "초월위습") ||
            !HasText(source, "e0IA", "unam", 894, "!0909_bijuuexplosion") ||
            !HasText(source, "e0IA", "utip", 895, "더미유닛"))
            throw new InvalidDataException(
                "WTS semantics do not distinguish transcendence from the dummy.");
    }

    private static bool HasField(
        W3uRecordEvidence record,
        string fieldId,
        int valueType,
        string value) =>
        record.Fields.Any(field => field.FieldId == fieldId &&
            field.ValueType == valueType && field.Value == value &&
            field.SanityValue == "00000000");

    private static bool HasText(
        MapSourceMetadata source,
        string objectId,
        string fieldId,
        int recordId,
        string resolvedText) =>
        source.WtsEvidence.Records.Any(record =>
            record.ObjectId == objectId && record.FieldId == fieldId &&
            record.RecordId == recordId && record.ResolvedText == resolvedText);

    private static void ValidateStory(
        MapSourceMetadata source,
        StoryProfileDocument story)
    {
        if (story.Source is null || story.Stages.IsDefault ||
            story.Source != new StorySourceReference(
                "2.314",
                ExpectedSourceMetadataSha256,
                source.Archive.Sha256,
                source.Jass.Sha256,
                source.W3u.Sha256,
                source.Wts.Sha256) ||
            story.Stages.Length != 14 ||
            !story.Stages.Select(stage => stage.Ordinal)
                .SequenceEqual(Enumerable.Range(1, 14)) ||
            !story.Stages.Select(stage => stage.ObjectiveRawcode)
                .SequenceEqual(ExpectedObjectiveRawcodes) ||
            !story.Stages.Select(stage => stage.MilestoneId)
                .SequenceEqual(ExpectedMilestones) ||
            story.Stages.Any(stage => stage.OwnerId != 5))
            throw new InvalidDataException("Story chronology is not authoritative.");

        foreach (var stage in story.Stages)
            ValidateStage(stage);

        var transcendenceRewards = story.Stages
            .SelectMany(stage => stage.RewardComponents.EveryPlayerBase
                .Select(component => (stage.Ordinal, Component: component)))
            .Where(item => item.Component.Kind == "Unit" &&
                item.Component.Id == "e01A")
            .ToArray();
        if (transcendenceRewards.Length != 1 ||
            transcendenceRewards[0].Ordinal != 10 ||
            transcendenceRewards[0].Component.Amount != 1 ||
            story.Stages.SelectMany(AllComponents)
                .Any(component => component.Id == "e0IA"))
            throw new InvalidDataException(
                "Story rewards confuse transcendence with the dummy.");

        var contractHash = ComputeContractSha256(story);
        if (!contractHash.Equals(ExpectedStoryContractSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Story semantic contract mismatch: {contractHash}");
    }

    private static void ValidateStage(StoryStage stage)
    {
        var groups = stage.RewardComponents;
        if (stage.RewardKinds.IsDefault || groups is null ||
            groups.EveryPlayerBase.IsDefault ||
            groups.ContributionAtLeast25Percent.IsDefault ||
            groups.Mvp.IsDefault || groups.HiddenOrSideEffect.IsDefault)
            throw new InvalidDataException("Story reward groups are missing.");

        var expectedKinds = groups.EveryPlayerBase
            .Where(component => component.Kind == "Unit")
            .Select(component => RewardKind(component.Id))
            .Where(kind => kind is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        if (!stage.RewardKinds.SequenceEqual(expectedKinds))
            throw new InvalidDataException("Story reward kinds do not match base rewards.");

        foreach (var component in AllComponents(stage))
        {
            if (string.IsNullOrWhiteSpace(component.Kind) ||
                component.Amount <= 0 || component.Limit < 0 ||
                component.ChanceNumerator <= 0 ||
                component.ChanceDenominator <= 0 ||
                component.ChanceNumerator > component.ChanceDenominator ||
                GreatestCommonDivisor(component.ChanceNumerator,
                    component.ChanceDenominator) != 1 ||
                string.IsNullOrWhiteSpace(component.Condition) ||
                component.Sources.IsDefault || component.Sources.IsEmpty ||
                component.Sources.Any(pin =>
                    string.IsNullOrWhiteSpace(pin.Function) ||
                    pin.StartLine <= 0 || pin.EndLine < pin.StartLine ||
                    pin.EvidenceLine < pin.StartLine ||
                    pin.EvidenceLine > pin.EndLine))
                throw new InvalidDataException("Story reward component is invalid.");
        }
    }

    private static IEnumerable<StoryRewardComponent> AllComponents(
        StoryStage stage) =>
        stage.RewardComponents.EveryPlayerBase
            .Concat(stage.RewardComponents.ContributionAtLeast25Percent)
            .Concat(stage.RewardComponents.Mvp)
            .Concat(stage.RewardComponents.HiddenOrSideEffect);

    private static string? RewardKind(string rawcode) => rawcode switch
    {
        "e016" => "special",
        "e017" => "uncommon",
        "e018" => "common_selectable",
        "e019" => "rare",
        "e01A" => "transcendence",
        "e0IX" => "random",
        _ => null
    };

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
            (left, right) = (right, left % right);
        return Math.Abs(left);
    }
}
