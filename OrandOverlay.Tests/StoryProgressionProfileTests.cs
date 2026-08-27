using OrandOverlay;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class StoryProgressionProfileTests
{
    private static readonly JsonSerializerOptions StrictJson = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    [Fact]
    public void SourcePinnedProfileLoadsFourteenOrderedOwnerFiveStages()
    {
        var profile = LoadShipped();

        Assert.Equal("2.314", profile.Source.MapVersion);
        Assert.Equal(
            "f9ddd3af7c0fbfd39b7a6df2ca0f5f295675bba5e8b91a5322dcec93d7ca8a83",
            profile.Source.Archive.Sha256);
        Assert.Equal(
            "0bccc47907a9505f38efaf6bbf20228a728eabdfaec3209cca7df2269bfc2028",
            profile.Source.JassSha256);
        Assert.Equal(3_069_240, profile.Source.JassLengthBytes);
        Assert.Equal(14, profile.Stages.Length);
        Assert.Equal("n00A", profile.Stages[8].ObjectiveRawcode);
        Assert.Equal("Marineford", profile.Stages[8].MilestoneId);
        Assert.All(profile.Stages, stage => Assert.Equal(5, stage.OwnerId));
        Assert.Equal(Enumerable.Range(1, 14),
            profile.Stages.Select(stage => stage.Ordinal));
    }

    [Fact]
    public void ShippedFilesHaveTheLoaderPinnedByteHashes()
    {
        Assert.Equal(
            "2d262ff929fcae3d70a94c6c76969708f37f28a62b9dd06b52b0af80d4c10c82",
            Sha256(DataPath("map-source-metadata-2314.json")));
        Assert.Equal(
            "8c24aff6b74ebc6a4117c9aa57bfc236cbd286d12ad85faf802c6453dc58ae0b",
            Sha256(DataPath("story-progression-2314.json")));
    }

    [Fact]
    public void LoaderNeedsOnlyTheTwoShippedProfilesAtRuntime()
    {
        var directory = CopyProfiles();
        try
        {
            Assert.Empty(Directory.GetFiles(directory, "*.w3x"));
            Assert.Empty(Directory.GetFiles(directory, "war3map.*"));
            Assert.Equal(14,
                MapStoryProfileLoader.LoadFromDirectory(directory).Stages.Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("missing-source")]
    [InlineData("missing-story")]
    [InlineData("malformed-story")]
    [InlineData("extra-property")]
    [InlineData("missing-stage")]
    [InlineData("extra-stage")]
    [InlineData("swapped-stages")]
    [InlineData("missing-component-group")]
    [InlineData("swapped-files")]
    [InlineData("source-mutated")]
    public void MalformedMissingExtraSwappedOrSourceMutatedDataFailsClosed(
        string mutation)
    {
        var directory = CopyProfiles();
        try
        {
            MutateFiles(directory, mutation);

            Assert.Throws<InvalidDataException>(() =>
                MapStoryProfileLoader.LoadFromDirectory(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("story-progression-2314.json")]
    [InlineData("map-source-metadata-2314.json")]
    public void AnyPinnedFileByteMutationFailsClosed(string fileName)
    {
        var directory = CopyProfiles();
        try
        {
            File.AppendAllText(Path.Combine(directory, fileName), " ");

            Assert.Throws<InvalidDataException>(() =>
                MapStoryProfileLoader.LoadFromDirectory(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void StoryStagesMatchIndependentFourGroupJassAuthorityTable()
    {
        var expected = ExpectedStages();
        var profile = LoadShipped();

        Assert.Equal(expected.Length, profile.Stages.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            var stage = profile.Stages[index];
            var authority = expected[index];
            Assert.Equal(authority.Rawcode, stage.ObjectiveRawcode);
            Assert.Equal(authority.Milestone, stage.MilestoneId);
            Assert.Equal(authority.RewardKinds, stage.RewardKinds);
            Assert.Equal(authority.EveryPlayerBase,
                stage.RewardComponents.EveryPlayerBase.Select(ComponentKey));
            Assert.Equal(authority.ContributionAtLeast25Percent,
                stage.RewardComponents.ContributionAtLeast25Percent
                    .Select(ComponentKey));
            Assert.Equal(authority.Mvp,
                stage.RewardComponents.Mvp.Select(ComponentKey));
            Assert.Equal(authority.HiddenOrSideEffect,
                stage.RewardComponents.HiddenOrSideEffect.Select(ComponentKey));
        }
    }

    [Fact]
    public void StageTenAwardsTranscendenceAndNeverTheMechanicalDummy()
    {
        var profile = LoadShipped();
        var stage = profile.Stages[9];

        Assert.Contains(stage.RewardComponents.EveryPlayerBase,
            component => component is
            {
                Kind: "Unit", Id: "e01A", Amount: 1,
                Sources: [{ Function: "vNN", EvidenceLine: 12965 }]
            });
        Assert.DoesNotContain(profile.Stages.SelectMany(AllComponents),
            component => component.Id == "e0IA");
        Assert.Equal(["random", "transcendence"],
            stage.RewardKinds.ToArray());
    }

    [Fact]
    public void ShippedSourceCarriesBoundedFullConsumptionW3uEvidence()
    {
        var source = LoadShipped().Source;
        var layout = source.W3uEvidence.Layout;

        Assert.Equal(new W3uParserLimits(
            536_870_912, 2, 100_000, 100_000, 1_048_576),
            source.W3uEvidence.ParserLimits);
        Assert.Equal(3, layout.Version);
        Assert.Equal(1_411, layout.TotalRecordCount);
        Assert.Equal(45_163, layout.TotalModificationCount);
        Assert.Equal(45_163, layout.ZeroSanityMarkerCount);
        Assert.Equal(857_425, layout.ConsumedBytes);
        Assert.Equal(0, layout.TrailingBytes);
        Assert.Equal(
        [
            new W3uTableEvidence("Original", 4, 1, 8, 93),
            new W3uTableEvidence("Custom", 93, 1_410, 97, 857_425)
        ], layout.Tables.ToArray());
        Assert.All(source.W3uEvidence.Records.SelectMany(record => record.Fields),
            field => Assert.Equal("00000000", field.SanityValue));
    }

    [Fact]
    public void W3uAndWtsEvidenceIndependentlyDistinguishesTranscendenceAndDummy()
    {
        var source = LoadShipped().Source;
        var transcendence = source.W3uEvidence.Records.Single(record =>
            record.ObjectId == "e01A");
        var dummy = source.W3uEvidence.Records.Single(record =>
            record.ObjectId == "e0IA");

        Assert.Equal((1_288, 768_517L, 768_786L, 15),
            (transcendence.RecordIndex, transcendence.StartOffset,
                transcendence.EndOffset, transcendence.ModificationCount));
        Assert.Contains(transcendence.Fields, field =>
            field is { FieldId: "uspe", ValueType: 0, Value: "1" });
        Assert.Contains(transcendence.Fields, field =>
            field is { FieldId: "uabi", ValueType: 3, Value: "Aeth,Avul" });

        Assert.Equal((676, 455_895L, 456_397L, 28),
            (dummy.RecordIndex, dummy.StartOffset, dummy.EndOffset,
                dummy.ModificationCount));
        Assert.Contains(dummy.Fields, field =>
            field is { FieldId: "umdl", ValueType: 3,
                Value: "bijuuexplosion.mdl" });
        Assert.Contains(dummy.Fields, field =>
            field is { FieldId: "utyp", ValueType: 3, Value: "mechanical" });

        AssertWtsText(source, "e01A", "unam", 2_118, "초월위습");
        AssertWtsText(source, "e01A", "utip", 2_119, "초월위습");
        AssertWtsText(source, "e0IA", "unam", 894,
            "!0909_bijuuexplosion");
        AssertWtsText(source, "e0IA", "utip", 895, "더미유닛");
    }

    [Fact]
    public void EveryRewardRawcodeHasResolvedW3uWtsSemantics()
    {
        var source = LoadShipped().Source;
        var expected = new Dictionary<string, (string Kind, string Text)>(
            StringComparer.Ordinal)
        {
            ["e0IX"] = ("RandomReward", "랜덤위습"),
            ["e0IA"] = ("MechanicalDummy", "!0909_bijuuexplosion"),
            ["e01A"] = ("TranscendenceReward", "초월위습"),
            ["e019"] = ("RareReward", "희귀위습"),
            ["e018"] = ("CommonSelectableReward", "흔함선택위습"),
            ["e017"] = ("UncommonReward", "안흔함위습"),
            ["e016"] = ("SpecialReward", "특별위습")
        };

        Assert.Equal(expected.Keys, source.W3uEvidence.Records
            .Select(record => record.ObjectId));
        foreach (var record in source.W3uEvidence.Records)
        {
            Assert.Equal(expected[record.ObjectId].Kind, record.SemanticKind);
            Assert.Contains(source.WtsEvidence.Records, text =>
                text.ObjectId == record.ObjectId && text.FieldId == "unam" &&
                text.ResolvedText == expected[record.ObjectId].Text);
        }
    }

    [Fact]
    public void SemanticValidationRejectsW3uLayoutFieldAndWtsMutationsWithoutByteHashShield()
    {
        var (source, story) = ReadContracts();
        var layoutMutation = source with
        {
            W3uEvidence = source.W3uEvidence with
            {
                Layout = source.W3uEvidence.Layout with { TrailingBytes = 1 }
            }
        };
        Assert.Throws<InvalidDataException>(() =>
            MapStoryProfileLoader.Validate(layoutMutation, story));

        var recordIndex = IndexOf(source.W3uEvidence.Records, record =>
            record.ObjectId == "e01A");
        var record = source.W3uEvidence.Records[recordIndex];
        var fieldIndex = IndexOf(record.Fields, field => field.FieldId == "uspe");
        var fieldMutation = source with
        {
            W3uEvidence = source.W3uEvidence with
            {
                Records = source.W3uEvidence.Records.SetItem(recordIndex,
                    record with
                    {
                        Fields = record.Fields.SetItem(fieldIndex,
                            record.Fields[fieldIndex] with { Value = "0" })
                    })
            }
        };
        Assert.Throws<InvalidDataException>(() =>
            MapStoryProfileLoader.Validate(fieldMutation, story));

        var sanityMutation = source with
        {
            W3uEvidence = source.W3uEvidence with
            {
                Records = source.W3uEvidence.Records.SetItem(recordIndex,
                    record with
                    {
                        Fields = record.Fields.SetItem(fieldIndex,
                            record.Fields[fieldIndex] with
                            {
                                SanityValue = "01000000"
                            })
                    })
            }
        };
        Assert.Throws<InvalidDataException>(() =>
            MapStoryProfileLoader.Validate(sanityMutation, story));

        var dummyIndex = IndexOf(source.W3uEvidence.Records, candidate =>
            candidate.ObjectId == "e0IA");
        var dummy = source.W3uEvidence.Records[dummyIndex];
        var swappedRecords = source.W3uEvidence.Records
            .SetItem(recordIndex, record with { ObjectId = "e0IA" })
            .SetItem(dummyIndex, dummy with { ObjectId = "e01A" });
        var swappedRecordMutation = source with
        {
            W3uEvidence = source.W3uEvidence with { Records = swappedRecords }
        };
        Assert.Throws<InvalidDataException>(() =>
            MapStoryProfileLoader.Validate(swappedRecordMutation, story));

        var textIndex = IndexOf(source.WtsEvidence.Records, text =>
            text.ObjectId == "e0IA" && text.FieldId == "utip");
        var textMutation = source with
        {
            WtsEvidence = source.WtsEvidence with
            {
                Records = source.WtsEvidence.Records.SetItem(textIndex,
                    source.WtsEvidence.Records[textIndex] with
                    {
                        ResolvedText = "초월위습"
                    })
            }
        };
        Assert.Throws<InvalidDataException>(() =>
            MapStoryProfileLoader.Validate(textMutation, story));
    }

    [Fact]
    public void SemanticValidationRejectsMissingExtraSwappedAndSourceMutatedRewards()
    {
        var (source, story) = ReadContracts();
        var stageIndex = 9;
        var stage = story.Stages[stageIndex];
        var baseIndex = IndexOf(stage.RewardComponents.EveryPlayerBase,
            component => component.Id == "e01A");
        var transcendence = stage.RewardComponents.EveryPlayerBase[baseIndex];

        var dummyMutation = ReplaceStage(story, stageIndex, stage with
        {
            RewardComponents = stage.RewardComponents with
            {
                EveryPlayerBase = stage.RewardComponents.EveryPlayerBase
                    .SetItem(baseIndex, transcendence with { Id = "e0IA" })
            }
        });
        Assert.Throws<InvalidDataException>(() =>
            MapStoryProfileLoader.Validate(source, dummyMutation));

        var sourceMutation = ReplaceStage(story, stageIndex, stage with
        {
            RewardComponents = stage.RewardComponents with
            {
                EveryPlayerBase = stage.RewardComponents.EveryPlayerBase
                    .SetItem(baseIndex, transcendence with
                    {
                        Sources = transcendence.Sources.SetItem(0,
                            transcendence.Sources[0] with { EvidenceLine = 12964 })
                    })
            }
        });
        Assert.Throws<InvalidDataException>(() =>
            MapStoryProfileLoader.Validate(source, sourceMutation));

        var extraMutation = ReplaceStage(story, stageIndex, stage with
        {
            RewardComponents = stage.RewardComponents with
            {
                EveryPlayerBase = stage.RewardComponents.EveryPlayerBase
                    .Add(transcendence)
            }
        });
        Assert.Throws<InvalidDataException>(() =>
            MapStoryProfileLoader.Validate(source, extraMutation));

        var swappedMutation = ReplaceStage(story, stageIndex, stage with
        {
            RewardComponents = stage.RewardComponents with
            {
                EveryPlayerBase = stage.RewardComponents.Mvp,
                Mvp = stage.RewardComponents.EveryPlayerBase
            }
        });
        Assert.Throws<InvalidDataException>(() =>
            MapStoryProfileLoader.Validate(source, swappedMutation));

        var missingMutation = story with
        {
            Stages = story.Stages.Where(item => item.Ordinal != 7)
                .ToImmutableArray()
        };
        Assert.Throws<InvalidDataException>(() =>
            MapStoryProfileLoader.Validate(source, missingMutation));
    }

    [Fact]
    public void LoadedStoryAndEvidenceCollectionsAreImmutableArrays()
    {
        Assert.Equal(typeof(ImmutableArray<StoryStage>),
            typeof(StoryProgressionProfile).GetProperty("Stages")!.PropertyType);
        Assert.Equal(typeof(ImmutableArray<StoryRewardComponent>),
            typeof(RewardComponentGroups).GetProperty("EveryPlayerBase")!
                .PropertyType);
        Assert.Equal(typeof(ImmutableArray<StoryRewardComponent>),
            typeof(RewardComponentGroups)
                .GetProperty("ContributionAtLeast25Percent")!.PropertyType);
        Assert.Equal(typeof(ImmutableArray<StoryRewardComponent>),
            typeof(RewardComponentGroups).GetProperty("Mvp")!.PropertyType);
        Assert.Equal(typeof(ImmutableArray<StoryRewardComponent>),
            typeof(RewardComponentGroups).GetProperty("HiddenOrSideEffect")!
                .PropertyType);
        Assert.Equal(typeof(ImmutableArray<W3uRecordEvidence>),
            typeof(W3uEvidence).GetProperty("Records")!.PropertyType);
        Assert.Equal(typeof(ImmutableArray<WtsRecordEvidence>),
            typeof(WtsEvidence).GetProperty("Records")!.PropertyType);
    }

    private static ExpectedStage[] ExpectedStages() =>
    [
        new("n000", "stage1", ["common_selectable"],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 180, "ActivePlayer", "LBN:6444-6457@6451"), C("Unit", "e018", 3, "ActivePlayer", "LBN:6444-6457@6452;RwE:6439-6443@6442")],
            [C("Unit", "e0IX", 1, "ActivePlayerAndDamagePercentAtLeast25", "y0E:6524-6534@6530;B9w:6434-6438@6437")],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 500, "MaximumDamageContributor", "AeG:6535-6555@6544"), C("Unit", "e0IX", 1, "MaximumDamageContributor", "AeG:6535-6555@6545;B9w:6434-6438@6437")], []),
        new("n002", "stage2", ["common_selectable"],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 800, "ActivePlayer", "vON:44224-44237@44231"), C("Unit", "e018", 3, "ActivePlayer", "vON:44224-44237@44232;RwE:6439-6443@6442")],
            [C("Unit", "e0IX", 1, "ActivePlayerAndDamagePercentAtLeast25", "dlE:44213-44223@44219;B9w:6434-6438@6437")],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 1500, "MaximumDamageContributor", "Ucw:44238-44258@44247"), C("Unit", "e0IX", 1, "MaximumDamageContributor", "Ucw:44238-44258@44248;B9w:6434-6438@6437")], []),
        new("n003", "stage3", ["common_selectable", "uncommon"],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 1000, "ActivePlayer", "RBE:16172-16186@16179"), C("Unit", "e018", 4, "ActivePlayer", "RBE:16172-16186@16180;RwE:6439-6443@6442"), C("Unit", "e017", 2, "ActivePlayer", "RBE:16172-16186@16181;G5E:16167-16171@16170")],
            [C("Unit", "e0IX", 1, "ActivePlayerAndDamagePercentAtLeast25", "a_w:16187-16197@16193;B9w:6434-6438@6437")],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 2500, "MaximumDamageContributor", "FIw:16198-16218@16207"), C("Unit", "e0IX", 1, "MaximumDamageContributor", "FIw:16198-16218@16208;B9w:6434-6438@6437")], []),
        new("n004", "stage4", ["special", "uncommon"],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 2000, "ActivePlayer", "Q8G:43508-43533@43519"), C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 1, "ActivePlayer", "Q8G:43508-43533@43521"), C("Unit", "e016", 2, "ActivePlayer", "Q8G:43508-43533@43526"), C("Unit", "e017", 1, "ActivePlayer", "Q8G:43508-43533@43522;G5E:16167-16171@16170")],
            [C("Unit", "e0IX", 1, "ActivePlayerAndDamagePercentAtLeast25", "S7G:43534-43544@43540;B9w:6434-6438@6437")],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 3000, "MaximumDamageContributor", "U9w:43545-43565@43554"), C("Unit", "e0IX", 1, "MaximumDamageContributor", "U9w:43545-43565@43555;B9w:6434-6438@6437")], []),
        new("n005", "stage5", ["special", "uncommon"],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 3000, "ActivePlayer", "Tew:40158-40191@40170"), C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 1, "ActivePlayer", "Tew:40158-40191@40172"), C("Unit", "e016", 2, "ActivePlayer", "Tew:40158-40191@40177"), C("Unit", "e017", 2, "ActivePlayer", "Tew:40158-40191@40173;G5E:16167-16171@16170")],
            [C("Unit", "e0IX", 1, "ActivePlayerAndDamagePercentAtLeast25", "icw:40192-40202@40198;B9w:6434-6438@6437")],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 5000, "MaximumDamageContributor", "R7G:40203-40223@40212"), C("Unit", "e0IX", 1, "MaximumDamageContributor", "R7G:40203-40223@40213;B9w:6434-6438@6437")],
            [C("Technology", "R01N", 1, "ActivePlayer", "Tew:40158-40191@40178"), C("RandomItem", "AI03", 1, "ActivePlayerAndMissingItem", "Tew:40158-40191@40181", denominator: 20)]),
        new("n006", "stage6", ["special", "uncommon"],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 4000, "ActivePlayer", "WeN:77368-77400@77380"), C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 3, "ActivePlayer", "WeN:77368-77400@77382"), C("Unit", "e016", 3, "ActivePlayer", "WeN:77368-77400@77387"), C("Unit", "e017", 2, "ActivePlayer", "WeN:77368-77400@77383;G5E:16167-16171@16170")],
            [C("Unit", "e0IX", 1, "ActivePlayerAndDamagePercentAtLeast25", "dON:77401-77411@77407;B9w:6434-6438@6437")],
            [C("Unit", "e018", 1, "MaximumDamageContributor", "voN:77444-77460@77451;RwE:6439-6443@6442")],
            [C("RandomItem", "AI00", 1, "ActivePlayerAndMissingItem", "WeN:77368-77400@77390", denominator: 20)]),
        new("n007", "stage7", ["rare"],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 6000, "ActivePlayer", "GGP:60235-60259@60246"), C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 4, "ActivePlayer", "GGP:60235-60259@60248"), C("Unit", "e019", 2, "ActivePlayer", "GGP:60235-60259@60252")],
            [C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 1, "ActivePlayerAndDamagePercentAtLeast25", "SQE:60260-60273@60268")],
            [C("Unit", "e018", 1, "MaximumDamageContributor", "iNw:60306-60322@60313;RwE:6439-6443@6442")], []),
        new("n008", "stage8", ["rare"],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 8000, "ActivePlayer", "gPw:65470-65504@65482"), C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 4, "ActivePlayer", "gPw:65470-65504@65484"), C("Unit", "e019", 3, "ActivePlayer", "gPw:65470-65504@65488")],
            [C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 1, "ActivePlayerAndDamagePercentAtLeast25", "y2E:65505-65518@65513")],
            [C("Unit", "e018", 1, "MaximumDamageContributor", "mhN:65519-65535@65526;RwE:6439-6443@6442")],
            [C("Technology", "R01O", 1, "ActivePlayer", "gPw:65470-65504@65489"), C("StockMaximumIncrement", "H0AW", 1, "ActivePlayer", "gPw:65470-65504@65491", limit: 8), C("RandomItem", "AI02", 1, "ActivePlayerAndMissingItem", "gPw:65470-65504@65494", denominator: 20)]),
        new("n00A", "Marineford", ["common_selectable"],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 9000, "ActivePlayer", "COw:59763-59784@59771"), C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 5, "ActivePlayer", "COw:59763-59784@59773"), C("Unit", "e018", 1, "ActivePlayer", "COw:59763-59784@59774;RwE:6439-6443@6442"), C("Unit", "h05Y", 1, "ActivePlayer", "COw:59763-59784@59775")],
            [C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 1, "ActivePlayerAndDamagePercentAtLeast25", "bON:59817-59830@59825")],
            [C("Unit", "e018", 1, "MaximumDamageContributor", "hrE:59831-59847@59838;RwE:6439-6443@6442")],
            [C("Technology", "R01P", 1, "ActivePlayer", "COw:59763-59784@59776"), C("StockMaximumIncrement", "H0AW", 1, "ActivePlayer", "COw:59763-59784@59778", limit: 8)]),
        new("n001", "stage10", ["random", "transcendence"],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 10000, "ActivePlayer", "vNN:12951-12974@12961"), C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 4, "ActivePlayer", "vNN:12951-12974@12963"), C("Unit", "e0IX", 2, "ActivePlayer", "vNN:12951-12974@12964;B9w:6434-6438@6437"), C("Unit", "e01A", 1, "ActivePlayer", "vNN:12951-12974@12965")],
            [C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 1, "ActivePlayerAndDamagePercentAtLeast25", "lEE:12937-12950@12945")],
            [C("Unit", "e0IX", 1, "MaximumDamageContributor", "ECE:12975-12992@12982;B9w:6434-6438@6437"), C("Unit", "e018", 1, "MaximumDamageContributor", "ECE:12975-12992@12983;RwE:6439-6443@6442")],
            [C("Technology", "R01Q", 1, "ActivePlayer", "vNN:12951-12974@12966"), C("StockMaximumIncrement", "H0AW", 1, "ActivePlayer", "vNN:12951-12974@12968", limit: 8)]),
        new("n00C", "stage11", ["common_selectable", "uncommon"],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 12500, "ActivePlayer", "Jcw:22196-22216@22204"), C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 4, "ActivePlayer", "Jcw:22196-22216@22206"), C("Unit", "e018", 3, "ActivePlayer", "Jcw:22196-22216@22207;RwE:6439-6443@6442"), C("Unit", "e017", 2, "ActivePlayer", "Jcw:22196-22216@22208;G5E:16167-16171@16170")],
            [C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 1, "ActivePlayerAndDamagePercentAtLeast25", "BLG:22182-22195@22190")],
            [C("Unit", "e0IX", 1, "MaximumDamageContributor", "Kcw:22249-22266@22256;B9w:6434-6438@6437"), C("Unit", "e018", 1, "MaximumDamageContributor", "Kcw:22249-22266@22257;RwE:6439-6443@6442")],
            [C("StockMaximumIncrement", "H0AW", 1, "ActivePlayer", "Jcw:22196-22216@22210", limit: 8)]),
        new("n00D", "stage12", ["common_selectable", "special"],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 10000, "ActivePlayer", "rgE:38812-38844@38824"), C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 3, "ActivePlayer", "rgE:38812-38844@38826"), C("TraitPoint", "PLAYER_STATE_RESOURCE_FOOD_USED", 1, "ActivePlayer", "rgE:38812-38844@38828"), C("Unit", "e018", 3, "ActivePlayer", "rgE:38812-38844@38829;RwE:6439-6443@6442"), C("Unit", "e016", 2, "ActivePlayer", "rgE:38812-38844@38833")],
            [C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 1, "ActivePlayerAndDamagePercentAtLeast25", "tNw:38845-38858@38853")],
            [C("Unit", "e0IX", 1, "MaximumDamageContributor", "Q6N:38859-38877@38866;B9w:6434-6438@6437"), C("Unit", "e018", 1, "MaximumDamageContributor", "Q6N:38859-38877@38867;RwE:6439-6443@6442")],
            [C("Technology", "R01S", 1, "ActivePlayer", "rgE:38812-38844@38834"), C("StockMaximumIncrement", "H0AW", 1, "ActivePlayer", "rgE:38812-38844@38836", limit: 8), C("MissionActivation", "MoriaKill", 1, "Always", "Q6N:38859-38877@38874")]),
        new("n00B", "stage13", ["common_selectable"],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 5000, "ActivePlayer", "N8E:62920-62940@62928"), C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 3, "ActivePlayer", "N8E:62920-62940@62930"), C("Unit", "e018", 2, "ActivePlayer", "N8E:62920-62940@62931;RwE:6439-6443@6442"), C("HeroExperience", "OwnedHeroes", 300, "ActivePlayer", "N8E:62920-62940@62932;n9G:65679-65681@65680")],
            [C("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", 1, "ActivePlayerAndDamagePercentAtLeast25", "JHE:62906-62919@62914")],
            [C("Unit", "e0IX", 1, "MaximumDamageContributor", "kDw:62987-63014@62995;B9w:6434-6438@6437"), C("Unit", "e018", 1, "MaximumDamageContributor", "kDw:62987-63014@62996;RwE:6439-6443@6442")],
            [C("StockMaximumIncrement", "H0AW", 1, "ActivePlayer", "N8E:62920-62940@62934", limit: 8), C("ConditionalTimerSeconds", "EggheadAppearance", 120, "DifficultyAtLeast5", "kDw:62987-63014@63012")]),
        new("n009", "stage14", [],
            [C("Gold", "PLAYER_STATE_RESOURCE_GOLD", 5000, "ActivePlayer", "KOE:8896-8914@8904"), C("HeroExperience", "OwnedHeroes", 300, "ActivePlayer", "KOE:8896-8914@8905;RNw:40602-40604@40603"), C("PermanentAbility", "A13A", 1, "ActivePlayer", "KOE:8896-8914@8908")],
            [C("Unit", "e0IX", 1, "ActivePlayerAndDamagePercentAtLeast25", "p2N:8947-8957@8953;B9w:6434-6438@6437")],
            [C("Unit", "e018", 1, "MaximumDamageContributor", "C4w:8958-8977@8968;RwE:6439-6443@6442")], [])
    ];

    private static string C(
        string kind,
        string id,
        long amount,
        string condition,
        string sources,
        int limit = 0,
        int numerator = 1,
        int denominator = 1) =>
        $"{kind}|{id}|{amount}|{limit}|{numerator}/{denominator}|{condition}|{sources}";

    private static string ComponentKey(StoryRewardComponent component) =>
        C(component.Kind, component.Id, component.Amount, component.Condition,
            string.Join(';', component.Sources.Select(source =>
                $"{source.Function}:{source.StartLine}-{source.EndLine}@{source.EvidenceLine}")),
            component.Limit, component.ChanceNumerator,
            component.ChanceDenominator);

    private static IEnumerable<StoryRewardComponent> AllComponents(
        StoryStage stage) =>
        stage.RewardComponents.EveryPlayerBase
            .Concat(stage.RewardComponents.ContributionAtLeast25Percent)
            .Concat(stage.RewardComponents.Mvp)
            .Concat(stage.RewardComponents.HiddenOrSideEffect);

    private static StoryProfileDocument ReplaceStage(
        StoryProfileDocument story,
        int index,
        StoryStage stage) =>
        story with { Stages = story.Stages.SetItem(index, stage) };

    private static int IndexOf<T>(
        ImmutableArray<T> values,
        Func<T, bool> predicate) =>
        values.Select((value, index) => (value, index))
            .Single(item => predicate(item.value)).index;

    private static void AssertWtsText(
        MapSourceMetadata source,
        string objectId,
        string fieldId,
        int recordId,
        string text) =>
        Assert.Contains(source.WtsEvidence.Records, record =>
            record.ObjectId == objectId && record.FieldId == fieldId &&
            record.RecordId == recordId && record.ResolvedText == text);

    private static (MapSourceMetadata Source, StoryProfileDocument Story)
        ReadContracts()
    {
        var source = JsonSerializer.Deserialize<MapSourceMetadata>(
            File.ReadAllBytes(DataPath("map-source-metadata-2314.json")),
            StrictJson)!;
        var story = JsonSerializer.Deserialize<StoryProfileDocument>(
            File.ReadAllBytes(DataPath("story-progression-2314.json")),
            StrictJson)!;
        MapStoryProfileLoader.Validate(source, story);
        return (source, story);
    }

    private static StoryProgressionProfile LoadShipped() =>
        MapStoryProfileLoader.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Data"));

    private static string DataPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Data", fileName);

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();

    private static string CopyProfiles()
    {
        var directory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "orand-story-" + Guid.NewGuid())).FullName;
        foreach (var name in new[]
        {
            "map-source-metadata-2314.json",
            "story-progression-2314.json"
        })
            File.Copy(DataPath(name), Path.Combine(directory, name));
        return directory;
    }

    private static void MutateFiles(string directory, string mutation)
    {
        var sourcePath = Path.Combine(directory,
            "map-source-metadata-2314.json");
        var storyPath = Path.Combine(directory, "story-progression-2314.json");
        switch (mutation)
        {
            case "missing-source":
                File.Delete(sourcePath);
                break;
            case "missing-story":
                File.Delete(storyPath);
                break;
            case "malformed-story":
                File.WriteAllText(storyPath, "{");
                break;
            case "extra-property":
            {
                var document = JsonNode.Parse(File.ReadAllText(storyPath))!;
                document["Unexpected"] = true;
                File.WriteAllText(storyPath, document.ToJsonString());
                break;
            }
            case "missing-stage":
            {
                var document = JsonNode.Parse(File.ReadAllText(storyPath))!;
                document["Stages"]!.AsArray().RemoveAt(6);
                File.WriteAllText(storyPath, document.ToJsonString());
                break;
            }
            case "extra-stage":
            {
                var document = JsonNode.Parse(File.ReadAllText(storyPath))!;
                document["Stages"]!.AsArray().Add(
                    document["Stages"]![0]!.DeepClone());
                File.WriteAllText(storyPath, document.ToJsonString());
                break;
            }
            case "swapped-stages":
            {
                var document = JsonNode.Parse(File.ReadAllText(storyPath))!;
                var stages = document["Stages"]!.AsArray();
                var first = stages[0]!.DeepClone();
                var second = stages[1]!.DeepClone();
                stages[0] = second;
                stages[1] = first;
                File.WriteAllText(storyPath, document.ToJsonString());
                break;
            }
            case "missing-component-group":
            {
                var document = JsonNode.Parse(File.ReadAllText(storyPath))!;
                document["Stages"]![0]!["RewardComponents"]!
                    .AsObject().Remove("MVP");
                File.WriteAllText(storyPath, document.ToJsonString());
                break;
            }
            case "swapped-files":
            {
                var source = File.ReadAllBytes(sourcePath);
                var story = File.ReadAllBytes(storyPath);
                File.WriteAllBytes(sourcePath, story);
                File.WriteAllBytes(storyPath, source);
                break;
            }
            case "source-mutated":
            {
                var document = JsonNode.Parse(File.ReadAllText(sourcePath))!;
                document["Archive"]!["Sha256"] = new string('0', 64);
                File.WriteAllText(sourcePath, document.ToJsonString());
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    private sealed record ExpectedStage(
        string Rawcode,
        string Milestone,
        string[] RewardKinds,
        string[] EveryPlayerBase,
        string[] ContributionAtLeast25Percent,
        string[] Mvp,
        string[] HiddenOrSideEffect);
}
