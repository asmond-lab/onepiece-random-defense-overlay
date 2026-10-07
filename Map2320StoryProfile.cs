using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
namespace OrandOverlay;

public enum Map2320Qualification { Unknown, UnconditionalActivePlayer, ActivePlayingUserCountAtCompletion }
public enum Map2320QualificationResult { Unknown, NotQualified, Qualified }
public sealed record Map2320PlayerAtCompletion(bool Active, bool Playing, bool UserControlled);
public sealed record Map2320Evidence(string Function, int StartLine, int EndLine, int EvidenceLine, string Text);
public sealed record Map2320Function(string Function, int StartLine, int EndLine, ImmutableArray<string> Lines);
public sealed record Map2320ObjectField(string Id, int Offset, int EndOffset, string Value);
public sealed record Map2320Object(string Id, int StartOffset, int EndOffset, string Name, ImmutableArray<Map2320ObjectField> Fields);
public sealed record Map2320Source(string MapVersion, ImmutableArray<SourceMemberPin> Members, ImmutableArray<Map2320Function> Functions, ImmutableArray<Map2320Object> Objects);
public sealed record Map2320StoryReward(string Kind, string Id, double Amount, int Limit, int ChanceNumerator, int ChanceDenominator, string Condition, string AmountExpression, bool Known, ImmutableArray<Map2320Evidence> Sources);
public sealed record Map2320StoryStage(int Ordinal, string ObjectiveRawcode, int OwnerId, string MilestoneId, string CompletionFunction, Map2320Qualification Qualification, ImmutableArray<Map2320Evidence> Evidence, ImmutableArray<Map2320StoryReward> EveryPlayerBase, ImmutableArray<Map2320StoryReward> ContributionQualified, ImmutableArray<Map2320StoryReward> Mvp, ImmutableArray<Map2320StoryReward> HiddenOrSideEffect)
{
    public Map2320QualificationResult Qualify(bool? active, double? damage, double? objectiveMaxLife, IReadOnlyList<Map2320PlayerAtCompletion>? completionPlayers)
    {
        if (active == false) return Map2320QualificationResult.NotQualified;
        if (active is null || Qualification == Map2320Qualification.Unknown) return Map2320QualificationResult.Unknown;
        if (Qualification == Map2320Qualification.UnconditionalActivePlayer) return Map2320QualificationResult.Qualified;
        if (completionPlayers is null || completionPlayers.Count != 4 || completionPlayers.Any(p => p is null) || damage is null || objectiveMaxLife is null || !double.IsFinite(damage.Value) || damage < 0 || !double.IsFinite(objectiveMaxLife.Value) || objectiveMaxLife <= 0) return Map2320QualificationResult.Unknown;
        int humans = completionPlayers.Count(p => p.Active && p.Playing && p.UserControlled);
        int threshold = humans == 2 ? 30 : humans == 3 ? 25 : 20;
        return damage.Value / objectiveMaxLife.Value >= threshold / 100d ? Map2320QualificationResult.Qualified : Map2320QualificationResult.NotQualified;
    }
}
public sealed record Map2320StoryProfile(int SchemaVersion, Map2320Source Source, ImmutableArray<Map2320StoryStage> Stages)
{
    public const string DataSha256 = "8715432b734c97c11c735d00337a3c8676f4b2e7060cb5de0870a2e025809b46";
    public static Map2320StoryProfile LoadBundled() => LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));
    public static Map2320StoryProfile LoadFromDirectory(string directory) => Load(File.ReadAllBytes(Path.Combine(directory, "story-progression-2320.json")));
    public static Map2320StoryProfile Load(byte[] bytes)
    {
        try
        {
            if (bytes is null || bytes.Length > 8_000_000) throw new InvalidDataException("Invalid story data size.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            RejectDuplicateFields(doc.RootElement);
            var profile = JsonSerializer.Deserialize<Map2320StoryProfile>(bytes, Options()) ?? throw new InvalidDataException("Empty story profile.");
            Validate(profile);
            if (Hash(bytes) != DataSha256) throw new InvalidDataException("Unapproved 2.320 story bytes.");
            return profile;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException or InvalidOperationException or OverflowException)
        { throw new InvalidDataException("Invalid 2.320 story contract.", ex); }
    }
    internal static JsonSerializerOptions Options()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(t => { if (t.Kind == JsonTypeInfoKind.Object) foreach (var p in t.Properties) p.IsRequired = true; });
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false, MaxDepth = 64, TypeInfoResolver = resolver };
        options.Converters.Add(new JsonStringEnumConverter(null, false));
        return options;
    }
    private static void RejectDuplicateFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject()) { Require(names.Add(p.Name), "Duplicate JSON property."); RejectDuplicateFields(p.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var e in element.EnumerateArray()) RejectDuplicateFields(e);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    private static readonly string[] Objectives = { "n000", "n002", "n003", "n004", "n005", "n006", "n007", "n008", "n00A", "n001", "n00C", "n00D", "n00B", "n009" };
    private static readonly int[] Gold = { 180,800,1000,2000,3000,4000,6000,8000,9000,10000,12500,10000,5000,5000 };
    private static readonly int[] Lumber = { 0,0,0,1,1,3,4,4,5,4,4,3,3,0 };
    private static readonly string[] Units = { "e018:3,e0IX:1", "e018:3,e0IX:1", "e017:2,e018:4,e0IX:1", "e016:2,e017:1", "e016:2,e017:2", "e016:3,e017:2", "e019:2", "e019:3", "e018:1,h05Y:1", "e01A:1,e0IX:2", "e017:2,e018:3", "e016:2,e018:3", "e018:2", "" };
    private static readonly string[] MvpUnits = { "e0IX:1","e0IX:1","e0IX:1","e0IX:1","e0IX:1","e018:1","e018:1","e018:1","e018:1","e018:1,e0IX:1","e018:1,e0IX:1","e018:1,e0IX:1","e018:1,e0IX:1","e018:1" };
    private static readonly int[] MvpGold = {500,1500,2500,3000,5000,0,0,0,0,0,0,0,0,0};
    private static string UnitSignature(IEnumerable<Map2320StoryReward> rewards) => string.Join(",", rewards.Where(r => r.Kind == "Unit").OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Id + ":" + r.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    internal static void Validate(Map2320StoryProfile p)
    {
        Require(p.SchemaVersion == 1 && p.Source.MapVersion == "2.320" && !p.Stages.IsDefault && p.Stages.Length == 14, "Wrong story identity.");
        Require(!p.Source.Functions.IsDefault && p.Source.Functions.Length == ApprovedFunctions.Count, "Closed source function set mismatch.");
        var functions = p.Source.Functions.ToDictionary(f => f.Function, StringComparer.Ordinal);
        foreach (var f in p.Source.Functions)
        {
            Require(ApprovedFunctions.TryGetValue(f.Function, out var expected) && expected == f.StartLine + ":" + f.EndLine + ":" + Hash(Encoding.UTF8.GetBytes(string.Join("\n", f.Lines))), "Unapproved source function.");
            Require(f.StartLine > 0 && f.EndLine <= 86599 && f.EndLine - f.StartLine + 1 == f.Lines.Length, "Invalid function range.");
        }
        Require(p.Source.Members.Length == 3 && string.Join("|", p.Source.Members.Select(m => m.Name+":"+m.LengthBytes+":"+m.Sha256)) == ApprovedMembers, "Unapproved source members.");
        Require(Hash(Encoding.UTF8.GetBytes(string.Join("\n", p.Source.Objects.Select(o => o.Id+":"+o.StartOffset+":"+o.EndOffset+":"+o.Name+":"+string.Join("|",o.Fields.Select(f=>f.Id+":"+f.Offset+":"+f.EndOffset+":"+f.Value)))))) == ApprovedObjectsHash, "Unapproved object evidence.");
        void CheckPin(Map2320Evidence pin)
        {
            Require(functions.TryGetValue(pin.Function, out var f) && pin.StartLine == f.StartLine && pin.EndLine == f.EndLine && pin.EvidenceLine >= f.StartLine && pin.EvidenceLine <= f.EndLine && pin.Text == f.Lines[pin.EvidenceLine-f.StartLine], "Unapproved source pin.");
        }
        for (int i=0;i<14;i++)
        {
            var s=p.Stages[i];
            Require(s.Ordinal == i+1 && s.ObjectiveRawcode == Objectives[i] && s.OwnerId == 5 && s.MilestoneId == (i==8?"Marineford":"stage"+(i+1)), "Story chronology mismatch.");
            Require(s.Qualification == (i<3?Map2320Qualification.UnconditionalActivePlayer:Map2320Qualification.ActivePlayingUserCountAtCompletion), "Wrong contribution rule.");
            Require(functions.ContainsKey(s.CompletionFunction) && s.Evidence.Length == 5, "Missing completion evidence.");
            foreach(var pin in s.Evidence) CheckPin(pin);
            Require(!s.EveryPlayerBase.IsDefault && !s.ContributionQualified.IsDefault && !s.Mvp.IsDefault && !s.HiddenOrSideEffect.IsDefault, "Missing group.");
            Require(s.EveryPlayerBase.All(r=>r.Condition=="ActivePlayer") && s.ContributionQualified.All(r=>r.Condition=="ContributionQualified") && s.Mvp.All(r=>r.Condition=="MaximumDamageContributor"), "Reward groups conflated.");
            Require(s.EveryPlayerBase.Where(r=>r.Kind=="Gold").Sum(r=>r.Amount)==Gold[i] && s.EveryPlayerBase.Where(r=>r.Kind=="Lumber").Sum(r=>r.Amount)==Lumber[i] && UnitSignature(s.EveryPlayerBase)==Units[i], "Base payout semantic mismatch.");
            Require(UnitSignature(s.Mvp)==MvpUnits[i] && s.Mvp.Where(r=>r.Kind=="Gold").Sum(r=>r.Amount)==MvpGold[i], "MVP payout mismatch.");
            Require(s.ContributionQualified.Length==(i<3?0:1) && s.ContributionQualified.All(r=>r.Amount==1 && r.Id==(i<6||i==13?"e0IX":"PLAYER_STATE_RESOURCE_LUMBER")), "Contribution payout mismatch.");
            foreach(var group in new[]{s.EveryPlayerBase,s.ContributionQualified,s.Mvp,s.HiddenOrSideEffect})
            {
                Require(group.Select(r=>r.Kind+":"+r.Id).Distinct(StringComparer.Ordinal).Count()==group.Length, "Duplicate reward.");
                foreach(var r in group)
                {
                    Require(new[]{"Gold","Lumber","Unit","FoodUsed","Technology","RandomItem","WorldGambleCharge","ClearScore","MvpCounter","StoryLifecycle","HeroExperience","PermanentAbility","CompletionDispatch","MissionNotice","ConditionalTimerSeconds"}.Contains(r.Kind) && !string.IsNullOrWhiteSpace(r.Id) && r.Id!="e0IA", "Unknown reward kind or dummy reward.");
                    Require(double.IsFinite(r.Amount) && r.Amount>0 && r.Amount<=1000000 && r.Amount==Math.Truncate(r.Amount) && r.Limit>=0 && r.Limit<=16 && r.ChanceNumerator==1 && (r.ChanceDenominator==1 || r.Kind=="RandomItem" && r.ChanceDenominator==20) && !string.IsNullOrWhiteSpace(r.Condition) && !string.IsNullOrWhiteSpace(r.AmountExpression) && !r.Sources.IsDefaultOrEmpty, "Invalid reward values.");
                    foreach(var pin in r.Sources) CheckPin(pin);
                }
            }
            Require(s.HiddenOrSideEffect.Any(r=>r.Kind=="ClearScore") && s.HiddenOrSideEffect.Any(r=>r.Kind=="MvpCounter"), "Missing side effects.");
        }
    }
    /// <summary>Planner-only compatibility projection. Never approves a legacy memory reader or source profile.</summary>
    public ImmutableArray<StoryStage> ProjectGuaranteedBaseStages() => Stages.Select(s =>
    {
        var rewards=s.EveryPlayerBase.Where(r=>r.Known && r.Condition=="ActivePlayer" && r.AmountExpression=="Constant" && r.ChanceNumerator==1 && r.ChanceDenominator==1 && (r.Kind is "Gold" or "Lumber" || r.Kind=="Unit" && RewardKind(r.Id)!=null)).Select(r=>new StoryRewardComponent(r.Kind,r.Id,checked((long)r.Amount),r.Limit,1,1,"ActivePlayer",r.Sources.Select(p=>new JassSourcePin(p.Function,p.StartLine,p.EndLine,p.EvidenceLine)).ToImmutableArray())).ToImmutableArray();
        return new StoryStage(s.Ordinal,s.ObjectiveRawcode,s.OwnerId,s.MilestoneId,rewards.Where(r=>r.Kind=="Unit").Select(r=>RewardKind(r.Id)!).Distinct(StringComparer.Ordinal).ToImmutableArray(),new RewardComponentGroups(rewards,[],[],[]));
    }).ToImmutableArray();
    private static string? RewardKind(string id) => id switch { "e016"=>"special","e017"=>"uncommon","e018"=>"common_selectable","e019"=>"rare","e01A"=>"transcendence","e0IX"=>"random",_=>null };
    private const string ApprovedMembers = "war3map.j:3014709:6fdfc64bf8ad9463f5b5c8a351ffa7e1875d6cf9b51210129539375fa77a6c7c|war3map.w3u:917401:18b5dba815b7baed4e56683136883e9b239fc4e8ac63491bc57696fb05586b2a|war3map.wts:1816858:4b9c531cde67c979eae0ba289d133862f57714cd7dc0cf3bbac3376b52ee6946";
private const string ApprovedObjectsHash = "5a67fcd4666b501ca1bc351d023dc7cd9fddf9d40fbb2de75913d7709116796a";
private static readonly Dictionary<string,string> ApprovedFunctions = new(StringComparer.Ordinal) {
["Akb"] = "76367:76377:cd346c402a5e9905d035b23794b954fdf9c9a03d9692f040d204b34daf8e3b9b",
["AuT"] = "17386:17392:4f612ab7bd89df93900726155193edf11eb96eb32b43c28b2aa62f379083dffa",
["B1T"] = "8791:8806:25703ed70dff1c01da09a80ea33b03dbb29be206893b200b7ab0869d04b94170",
["DAy"] = "10773:10788:baecacee8891f91fbefe300d4c0dba81e2cd96405f64f0e58603a1b99ae1ee60",
["Dey"] = "41597:41679:61f3682d448cf7464a910019f135c217d495ffd6959ff2e8ab21673d5edb7be7",
["Dub"] = "46349:46419:127e844084fa6113b129c81558e94ca792e5537ba0760f95b5b854ed1adddb6c",
["E7T"] = "23603:23613:d2402f425b0710756b0cd9d0472a398af7f70307fa1f950181b0c37ccf577a12",
["Eiy"] = "12141:12145:dc88d0e7690112be603caa0a0b47c05bb13f16c56123d9170dee48a56b06d611",
["EnT"] = "79314:79324:87840b852d112e6e66bdabef4c4c3250af82dd03b9e244271b28a1516526e09d",
["F6y"] = "4086:4091:e130e2f951dbddd51289caf8c745fa6444e243a20adf2b90ff272c40123259be",
["GgF"] = "17407:17438:7f86c866b5bc68ca703c8ed24337ee950cd13f6fc14f24fe47dddb01d44a74c2",
["GnT"] = "16462:16499:69cb56df2013cf8fc7a32a9418673dddfe1dc5b615c2c5b21f390ee2af1ab2c5",
["IOb"] = "17544:17550:162a606b8de69ae69ec78f1b3f0384ac2dc8e94054fac557ed31653cda7a37ee",
["Iwb"] = "18326:18332:eb826254708075203b0603a5327920951cd99a5df8ec3a58c9a6c207979597fa",
["KAT"] = "7591:7601:7adcae8bb80d24f89c53fc851eaac236df1287ed50fe4013411b4e1ecc9a3327",
["L5F"] = "21640:21646:88c12be9b03e2a7274c0775927c556a79659785c98d7fb8e953eb72d0607fab4",
["MVy"] = "14362:14365:268239e12d2b1d0d05743e54b7b5aa62cba22502439dd9be4de856d9a6ae2540",
["M_T"] = "23508:23514:c0068da4623544df2c8df3e19a44f414d14fec52aad7b85fc774510de4a2ca7c",
["MbT"] = "23686:23692:59c3592afe9684b05cf456dadebeec2d222217d2e59a48838dbf1a45db8cd639",
["N3T"] = "21539:21639:c106e71eb387cf2ec2bae8d61523df89e3f3727672bcee326012f3aa56b049c2",
["NNb"] = "17439:17543:9c0c2efbb144fb7778335d346b495901c1eb0b589c13cd75ca4b70031c229917",
["NTF"] = "47807:47911:3a6ecb89a3b251e50b4101979b70659b01367faf605483a95427389183bf4c52",
["OIy"] = "26027:26042:401087cff913544b84e318f27cd1d729b6e673317079569e29bae617b8b9ce29",
["QBT"] = "27906:27908:d5c443e3f32ff83e6ed38e4b6f14e6bf9bbf268418a0f68fb7dad38274406422",
["TGT"] = "32527:32542:ac1e8ec58f4b0e81e734c1480262a665391afb735dcdbc22d16a43de635883a4",
["Tfb"] = "33217:33232:62c0dd31f9e855ff07946820af051fecec9595d7cc386f9c598755658219120b",
["Vmy"] = "37136:37142:b62046a8a7ae47883ddd79e0d6f56604187bbd1169da71c889468db524cd5003",
["W2F"] = "37492:37507:37c749d4faa2e343f2646a12868acecbdd173943946cefa6d819f908a9dc8a60",
["YMb"] = "40796:40802:3ac307bcfef14548fab8f0549e7f22f1290ead0ae539f9c0e946e0c8d1e95932",
["YTF"] = "37045:37135:02b752303818a7ae8e5c4fd510551b4ea254300cba00318952ea0897f0cccb38",
["ZAy"] = "18253:18325:33940c777094c1a376f6b720dae719abf07c8f23beb723af7a547e301e4cf2ae",
["ZVy"] = "41680:41686:6804b93b2a04b0c0c2b5d410283ab8f6cb098947a1632b884bf055dc04e397e1",
["ZbT"] = "23394:23507:03505bd4a30f4dabde4661c6e229709d663d7f63976d1369c34a95e2f9c87548",
["Zlb"] = "37034:37044:a3a3cae8337bfb55134e4fd9394223188bfda69babcec84c234c939254164d78",
["b9b"] = "43768:43790:f410b920f96619fa4918c9c2d231c7028f710d61406475cef3236a8766af496a",
["bOT"] = "76378:76473:bfc1df503a87109c02ac9507ad8cd6b76c6dc582a5e6614b8d28f8b2a6fa06ab",
["bnT"] = "14709:14711:2a3d32af452879571cc4434cf4aad94f770b6b972b2bb65c1750cee51c5dd26f",
["d4F"] = "18191:18201:c4f25f726427d3ca3fd46dc9f74cbe96c1617e0c18b0f66b2db81c5f0173b374",
["dsb"] = "46420:46426:8591c55bd16a9de3ee77f9f6af366b8b4a2c27ca5f96979d1450cbceafcb1f19",
["eYT"] = "47183:47198:24421acd7f306d8099ab6428eb139584fccc19fa2441b11c44212457f5c7b3b0",
["eub"] = "47912:47918:6b88ad2c0858b6f1069e3a907980a3c258df3976283ac8280142cad77277e936",
["fbF"] = "48987:49002:97e098a6e28db7d024dd3f4dbff735631cc570241a26e9e37fe4c398c3870b06",
["fqT"] = "40713:40795:1c26da71aed722b721c38dd207aa3bbf0387b6e48cda53ac71846833ef50a66d",
["gpy"] = "11194:11198:846c6eb3e66df3f5dd000943bf3d4aa3df78ce6fad5d76ea71255ac2f4ef3a8c",
["i0y"] = "16392:16440:75ff47275f4faec51a8b7e49802f3498e50bcb438d948d1fbb9e2ddd92ca9843",
["iFF"] = "41546:41596:2ccd7b24ba8e059089a91f7d51a3ae25e1e35fde3261f76f10f4bd88f1f305bd",
["iJy"] = "23614:23685:2f53b1afc24d48334f3352f040ae008d536259337fa9a49482aebb16e25a3ecb",
["iSb"] = "80140:82544:f0d250e280848146fa4d383e4932a7dca865296a6a6e95ff88319e55394f525a",
["ilT"] = "18202:18252:165dcd6a12f2bbb3068bbd108ac812ac6857bc56df33f2f69ab856d28c7792d2",
["inb"] = "51983:51998:30210bbb0cc559faec599438c0184b5c2c592fd0cb4971cfab7ec5aebd9ffcb4",
["j0y"] = "9368:9372:1ead2ea93ef879aa955e7f5786795672fff3770c016ea7cd709c12c096960d56",
["jLy"] = "52721:52762:2a1d73491699c2db5e3f39db116e2e08014c78cb8994ef4a3d9f8732c86a34ce",
["jib"] = "15705:15719:ec0cb0783590851439199314fcbbe32d3776dfd1eed1e6f5ef87b029f2972dbb",
["jwb"] = "16441:16445:f1d402b1e922d3a2a1f209f89b5bc124f0d969a3622f70f186fb4f57eb240a1a",
["k3y"] = "53340:53355:42e01c4aa5718fe95889585c951c939c3ab061b0992d9b902a69429d5fd83b8e",
["kOb"] = "58380:58491:03ef01a22aed07f8395430d4878a8c26c7382f40968b2046a7d3bc3852997cb6",
["ley"] = "55167:55182:0aa31676bc97afa46cae38ca373e1628ac14c549963283cd729433c83255e95d",
["mWb"] = "56497:56512:65d30f38ee23e48b376f78230b4091234ae31304cd4accb7f2e15b57833a0915",
["main"] = "83460:86375:c7c0ba131563ab321a3540475d04a1113dfcd82f880bf5ece8e4a8fda48794ea",
["oIT"] = "58204:58219:d3e56d497a86fe59c08ffb0c153024e5074372d10159ea5b621d104a7ad8cd83",
["oZT"] = "58492:58498:3a3bc8e02fb82f6b5c55abca9aa9bda3983a5537b48e0d68a3fa1529e5b7b63c",
["pmT"] = "79375:79446:9f69e4b65bc2028cc6e2ce9ba618becc8b62b3c4cfb6524b21a395a8ddfab9d5",
["ptT"] = "6993:7001:28568a5d1d08dc479f1785b34adb58e30e08ee7fd8f7a186a7e59059f62a0f25",
["tZb"] = "16446:16461:7e20ca9019ff7738b7256c6eadcdf05203e1419cdcdc31c5f01a3301d0e5e2b0",
["tkT"] = "40702:40712:9a4cd4a8574ec95aeec6a6a2835e89554cf5216f6eb45d4fdabc755236730c33",
["uTT"] = "10413:10417:98608219ca68552120445016104be134e0d1152c4431e138149209483ae89924",
["ulb"] = "52620:52720:791308ee030f5c36305840d4de7c3f0266783c00161a6ff2b0478f634397540b",
["vkb"] = "76474:76480:01718b6bc23f6a812f7ead6ea11617643517cb2f60ec4e888900f8d406c6d640",
["w6y"] = "79325:79374:e4d6dc50777cdbfb800dbc45f284530f1b4032112101694ecc490582ca47697f",
["x1y"] = "46338:46348:56b0768bc9a9ba20cf3c33ac2edc3f1ba674127b7d8c7d440b598c0ef68bef8f",
["yDy"] = "79447:79453:8d8e9688c4ba9e55bbee67b7e85992ebb69a08da3cc4662eb454cb926ef65783",
["yoy"] = "79975:79977:5643b66a1a1e8448ebe411010d0cbeac5ed1091366a903143d1eb9776d555439"
};
}
