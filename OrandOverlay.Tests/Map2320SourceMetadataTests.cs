using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2320SourceMetadataTests
{
    private static byte[] BundledBytes() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", Map2320SourceMetadata.DataFileName));
    private static JsonObject Tree() => JsonNode.Parse(BundledBytes())!.AsObject();
    private static void Reject(JsonNode node) => Assert.Throws<InvalidDataException>(() => Map2320SourceMetadata.Load(Encoding.UTF8.GetBytes(node.ToJsonString())));

    [Fact]
    public void BundledPinsAndCompleteLayoutAreImmutableAndExact()
    {
        var m = Map2320SourceMetadata.LoadBundled();
        Assert.Equal("2.320", m.MapVersion);
        Assert.Equal(8, m.Members.Length);
        Assert.Equal(3014709, m.Jass.LengthBytes);
        Assert.Equal(917401, m.W3u.LengthBytes);
        Assert.Equal(1816858, m.Wts.LengthBytes);
        Assert.Equal(1518, m.W3uEvidence.Layout.TotalRecordCount);
        Assert.Equal(48313, m.W3uEvidence.Layout.TotalModificationCount);
        Assert.Equal(48313, m.W3uEvidence.Layout.ZeroSanityMarkerCount);
        Assert.Equal(10990, m.WtsEvidence.ParsedRecordCount);
        Assert.Equal(14, m.WtsEvidence.Records.Length);
        Assert.Equal(18, m.JassPins.Length);
        Assert.Equal(new[] { 8792,37493,55168,32528,51984,47184,48988,58205,10774,56498,26028,53341,33218,43769 },m.JassPins.Take(14).Select(x=>x.StartLine));
        Assert.Equal(85353,m.JassPins[14].StartLine);
        Assert.Equal(85366,m.JassPins[14].EndLine);
        Assert.Equal(new[] {10413,11194,12141},m.JassPins.Skip(15).Select(x=>x.StartLine));
        Assert.Equal(Map2320SourceMetadata.SemanticSha256, Map2320SourceMetadata.ComputeSemanticHash(m));
        Assert.Equal(Map2320SourceMetadata.DataSha256, Convert.ToHexString(SHA256.HashData(BundledBytes())).ToLowerInvariant());
        foreach(var r in m.W3uEvidence.Records) Assert.Equal(r.ModificationCount,r.Fields.Length);
        Assert.Equal("MechanicalDummyNotReward",m.W3uEvidence.Records.Single(r=>r.ObjectId=="e0IA").SemanticKind);
        Assert.Contains(m.W3uEvidence.Records.Single(r=>r.ObjectId=="e0IA").Fields,f=>f.FieldId=="utyp"&&f.Value=="mechanical");
        Assert.Equal("TranscendenceReward",m.W3uEvidence.Records.Single(r=>r.ObjectId=="e01A").SemanticKind);
    }

    [Fact]
    public void RoundTripAllowsFormattingAndPropertyOrderOnly()
    {
        var m=Map2320SourceMetadata.LoadBundled();
        var round=Map2320SourceMetadata.Load(JsonSerializer.SerializeToUtf8Bytes(m));
        Assert.Equal(Map2320SourceMetadata.SemanticSha256,Map2320SourceMetadata.ComputeSemanticHash(round));
        var root=Tree(); var reversed=new JsonObject();
        foreach(var property in root.Reverse()) reversed.Add(property.Key,property.Value?.DeepClone());
        Assert.Equal("2.320",Map2320SourceMetadata.Load(Encoding.UTF8.GetBytes(reversed.ToJsonString())).MapVersion);
    }

    [Theory]
    [InlineData("StartOffset")][InlineData("EndOffset")][InlineData("ModificationCountOffset")]
    [InlineData("RecordIndex")][InlineData("ModificationCount")][InlineData("Reserved0")][InlineData("Reserved1")]
    public void EveryRecordOffsetAndHeaderMutationRejected(string property)
    {
        for(int i=0;i<7;i++){var n=Tree();var r=n["W3uEvidence"]!["Records"]![i]!;r[property]=r[property]!.GetValue<long>()+1;Reject(n);}
    }

    [Theory]
    [InlineData("StartOffset")][InlineData("ValueOffset")][InlineData("SanityOffset")][InlineData("EndOffset")][InlineData("ValueType")]
    public void EveryFieldOffsetMutationRejected(string property)
    {
        for(int i=0;i<7;i++){var n=Tree();var f=n["W3uEvidence"]!["Records"]![i]!["Fields"]![0]!;f[property]=f[property]!.GetValue<long>()+1;Reject(n);}
    }

    [Theory]
    [InlineData("RecordStartOffset")][InlineData("RecordEndOffset")][InlineData("TextStartOffset")][InlineData("TextEndOffset")][InlineData("RecordId")]
    public void EveryWtsOffsetMutationRejected(string property)
    {
        for(int i=0;i<14;i++){var n=Tree();var w=n["WtsEvidence"]!["Records"]![i]!;w[property]=w[property]!.GetValue<long>()+1;Reject(n);}
    }

    [Fact]
    public void WrongHashDummyRewardDuplicatesUnknownMissingTypesAndRangesFailClosed()
    {
        var n=Tree();n["Members"]![0]!["Sha256"]=new string('0',64);Reject(n);
        n=Tree();n["Archive"]!["Sha256"]=new string('0',64);Reject(n);
        n=Tree();n["WtsEvidence"]!["Records"]![0]!["RecordSha256"]=new string('0',64);Reject(n);
        n=Tree();n["W3uEvidence"]!["Records"]![1]!["SemanticKind"]="TranscendenceReward";Reject(n);
        n=Tree();n["Members"]!.AsArray().Add(n["Members"]![0]!.DeepClone());Reject(n);
        n=Tree();n["W3uEvidence"]!["Records"]![0]!["Unknown"]=1;Reject(n);
        n=Tree();n.Remove("MapVersion");Reject(n);
        n=Tree();n["Archive"]!["LengthBytes"]="119129261";Reject(n);
        n=Tree();n["Archive"]!["LengthBytes"]=-1;Reject(n);
        n=Tree();n["Archive"]!["LengthBytes"]=1.5;Reject(n);
        n=Tree();n["WtsEvidence"]=null;Reject(n);
        var text=Encoding.UTF8.GetString(BundledBytes());
        Assert.Throws<InvalidDataException>(()=>Map2320SourceMetadata.Load(Encoding.UTF8.GetBytes(text.Replace("\"MapVersion\": \"2.320\"","\"MapVersion\": \"2.320\", \"MapVersion\": \"2.320\""))));
        Assert.Throws<InvalidDataException>(()=>Map2320SourceMetadata.Load(Encoding.UTF8.GetBytes(text.Replace("\"Reserved0\": 1","\"Reserved0\": 1, \"Reserved0\": 1"))));
        Assert.Throws<InvalidDataException>(()=>Map2320SourceMetadata.Load(new byte[Map2320SourceMetadata.MaxMetadataBytes+1]));
    }

    [Fact]
    public void OptionalExtractedSourcesVerifyFullBytesAndWtsRecordSlices()
    {
        // Packaged tests never depend on docs. Set this to the extracted member directory for offline source reproduction.
        var directory=Environment.GetEnvironmentVariable("ORAND_2320_SOURCE_MEMBERS");
        if(string.IsNullOrEmpty(directory)) return;
        var m=Map2320SourceMetadata.LoadBundled();
        var members=m.Members.ToDictionary(p=>p.Name,p=>File.ReadAllBytes(Path.Combine(directory,p.Name)));
        Map2320SourceMetadata.ValidateSourceMembers(m,members);
        var w=members["war3map.wts"];
        foreach(var r in m.WtsEvidence.Records)
        {
            Assert.Equal(r.RecordSha256,Convert.ToHexString(SHA256.HashData(w.AsSpan((int)r.RecordStartOffset,(int)(r.RecordEndOffset-r.RecordStartOffset)))).ToLowerInvariant());
            Assert.Equal(r.ResolvedText,Encoding.UTF8.GetString(w,(int)r.TextStartOffset,(int)(r.TextEndOffset-r.TextStartOffset)));
        }
        var u=members["war3map.w3u"];
        foreach(var r in m.W3uEvidence.Records)
        {
            Assert.Equal(r.BaseId,Encoding.ASCII.GetString(u,(int)r.StartOffset,4));
            Assert.Equal(r.ObjectId,Encoding.ASCII.GetString(u,(int)r.StartOffset+4,4));
            Assert.Equal(r.ModificationCount,BitConverter.ToInt32(u,(int)r.ModificationCountOffset));
            foreach(var f in r.Fields)
            {
                Assert.Equal(f.FieldId,Encoding.ASCII.GetString(u,(int)f.StartOffset,4));
                Assert.Equal(f.ValueType,BitConverter.ToInt32(u,(int)f.StartOffset+4));
                Assert.Equal(f.SanityValue,Convert.ToHexString(u.AsSpan((int)f.SanityOffset,4)).ToLowerInvariant());
            }
        }
        members["war3map.j"][0]^=1;
        Assert.Throws<InvalidDataException>(()=>Map2320SourceMetadata.ValidateSourceMembers(m,members));
    }
}
