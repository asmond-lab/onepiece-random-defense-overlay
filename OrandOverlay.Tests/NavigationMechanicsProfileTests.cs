using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class NavigationMechanicsProfileTests
{
    [Fact]
    public void SourcePinnedImmutableProfileLoadsCompleteExactBindings()
    {
        var profile = Load();

        Assert.Equal("2.314", profile.Source.MapVersion);
        Assert.Equal(
            "0bccc47907a9505f38efaf6bbf20228a728eabdfaec3209cca7df2269bfc2028",
            profile.Source.JassSha256);
        Assert.Equal(3_069_240, profile.Source.JassLengthBytes);
        Assert.Equal(88_593, profile.Source.JassLineCount);
        Assert.Equal(5, profile.Source.Members.Length);
        Assert.Equal(30, profile.Source.Ranges.Length);
        Assert.Equal(5, profile.Categories.Length);
        Assert.Equal(15, profile.Options.Length);
        Assert.Equal(16, profile.Pools.Length);
        Assert.Equal(16, profile.Transitions.Length);
        Assert.Equal(15, profile.Formulas.Length);
        Assert.Equal(30, profile.VerificationFixtures.Length);
        Assert.Equal(15, profile.Bindings.Length);
        Assert.Equal("AlliedForces.DoubleBenefit", profile.ForcedRound24OptionId);
        Assert.Equal(new Rational(1, 12), profile.RandomSelectionProbability.Value);
        Assert.Equal(new Rational(1, 1), profile.RandomSelectionMass);
        Assert.Equal([1, 9, 17], profile.Bounty.PityNumeratorCheckpoints.ToArray());
        Assert.Equal([12, 6, 4, 2],
            profile.ContinuousBetting.MilestonePrecedence.ToArray());
        Assert.Equal(WaveDisposition.NoSafeRecommendation,
            profile.GetWaveDisposition().Disposition);
        Assert.False(profile.CanSafelyRecommendWave);

        var pity = profile.Source.Ranges.Single(range => range.Id == "bounty-pity");
        Assert.Equal((13_791, 13_842, NavigationSourcePurpose.BountyPityRecurrence),
            (pity.StartLine, pity.EndLine, pity.Purpose));
        var chest = profile.Source.Ranges.Single(range => range.Id == "bounty-chest-loot");
        Assert.Equal((60_837, 60_876, NavigationSourcePurpose.BountyChestLoot),
            (chest.StartLine, chest.EndLine, chest.Purpose));
        Assert.Equal(typeof(ImmutableArray<NavigationPool>),
            typeof(NavigationMechanicsProfile).GetProperty(nameof(profile.Pools))!.PropertyType);
        Assert.Equal(typeof(NavigationVerificationInput),
            typeof(NavigationVerificationFixture).GetProperty("Input")!.PropertyType);
        Assert.Equal(typeof(NavigationVerificationExpected),
            typeof(NavigationVerificationFixture).GetProperty("Expected")!.PropertyType);
        Assert.Null(typeof(NavigationVerificationFixture).GetProperty("Checksum"));
        Assert.DoesNotContain(typeof(NavigationMechanicsProfile).GetProperties(),
            property => property.PropertyType.IsArray ||
                property.PropertyType.IsGenericType &&
                property.PropertyType.GetGenericTypeDefinition() == typeof(List<>));
        Assert.True(typeof(NavigationMechanicsProfile).IsSealed);

        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../Data/navigation-mechanics-2314.json"));
        var shippedPath = Path.Combine(AppContext.BaseDirectory, "Data",
            "navigation-mechanics-2314.json");
        Assert.Equal(File.ReadAllBytes(sourcePath), File.ReadAllBytes(shippedPath));
    }

    [Fact]
    public void EveryPoolHasExactReducedNormalizedProbabilityMass()
    {
        var profile = Load();

        foreach (var pool in profile.Pools)
        {
            var mass = new OracleRational(0, 1);
            foreach (var member in pool.Members)
            {
                var numerator = BigInteger.Parse(member.Probability.Numerator);
                var denominator = BigInteger.Parse(member.Probability.Denominator);
                Assert.True(numerator > 0);
                Assert.True(denominator > 0);
                Assert.Equal(BigInteger.One,
                    BigInteger.GreatestCommonDivisor(numerator, denominator));
                Assert.Equal(numerator.ToString(), member.Probability.Numerator);
                Assert.Equal(denominator.ToString(), member.Probability.Denominator);
                mass += new OracleRational(numerator, denominator);
            }
            Assert.Equal(new OracleRational(1, 1), mass);
        }

        Assert.Equal(new Rational(84, 125), profile.Pool("casino-normal").Members
            .Single(member => member.Id == "random-rare").Probability.Value);
        Assert.Equal(new Rational(2079, 3125), profile.Pool("casino-isekai").Members
            .Single(member => member.Id == "random-rare").Probability.Value);
        Assert.Equal(new Rational(2, 25), profile.Pool("royal-proc").Members
            .Single(member => member.Id == "proc").Probability.Value);
        Assert.All(profile.Pools.Where(pool => pool.Kind == NavigationPoolKind.UniformDispatch),
            pool =>
            {
                Assert.Equal(12, pool.Members.Length);
                Assert.All(pool.Members, member =>
                    Assert.Equal(new Rational(1, 12), member.Probability.Value));
            });
    }

    [Fact]
    public void ExtractedHelperFactsAreTypedAndSemanticallyPinned()
    {
        var profile = Load();
        var buster = profile.HelperSpells.Single(spell => spell.Id == "A0BZ");

        Assert.Equal(NavigationKnowledgeState.Known, buster.State);
        Assert.Equal("damage:7000000,radius:450", buster.Levels[0].Value);
        Assert.Equal("damage:10000000,radius:450", buster.Levels[1].Value);
        Assert.Equal(["best-help-selection", "buster-call-effect"],
            buster.SourceRangeIds.ToArray());
        Assert.Equal(NavigationUnknownReason.NoStaticWaveRoster,
            profile.WaveCombat.RemainingEffectiveHp.Reason);
        Assert.Equal(NavigationUnknownReason.DynamicGroupEnumeration,
            profile.Royal.TargetCount.Reason);
    }

    [Fact]
    public void ActualArchiveMembersAreReadInMemoryAndMatchEveryPinnedValue()
    {
        var actual = Task2MpqMemberEvidence.Read(
            @"C:\Users\123\Documents\Warcraft III\Maps\Download\ORDR_S2_2.314[R].w3x",
            ["war3map.w3a", "war3map.w3u", "war3map.wts", "war3map.j",
                "war3mapMisc.txt"]);
        var profile = Load();

        Assert.Equal(profile.Source.Members.Length, actual.Count);
        foreach (var expected in profile.Source.Members)
        {
            var member = Assert.Single(actual, value => value.Name == expected.Name);
            Assert.Equal(expected.Length, member.Length);
            Assert.Equal(expected.Sha256, member.Sha256);
        }
        Assert.DoesNotContain(actual, member => member.Name == "extracted.tmp");
    }

    [Fact]
    public void ReleaseLoaderOracleSurfaceConsumesAllTypedFixturesAndRejects4097()
    {
        var profile = Load();
        OracleExecutionResult? bounty = null;

        foreach (var fixture in profile.VerificationFixtures)
        {
            var result = Task2ReferenceOracle.Execute(profile, fixture);
            AssertExpected(fixture, result);
            if (fixture.Id == "bounty-score-4096")
                bounty = result;
        }

        Assert.NotNull(bounty);
        Assert.Equal(4096, bounty!.ExecutedKills);
        Assert.Equal(new OracleRational(1, 1), bounty.Mass);
        Assert.Equal(1251, bounty.TerminalStateCount);
        Assert.Equal(4_342_221, bounty.TransitionCount);
        Assert.Equal(108_853, bounty.VarianceDenominatorBeforeReductionBits);
        Assert.Equal(108_858, bounty.MaxObservedRationalBits);
        Assert.Equal(169, bounty.StandardDeviation);
        Assert.Equal(553, bounty.Quantile95);
        Assert.Equal((7500, 5111, 499, 10000, 5279, 5881),
            (bounty.FloorBp, bounty.ValueBp, bounty.UpperBp, bounty.TopBp,
                bounty.CaptureConflictBp, bounty.NavigationScore));

        var invalid = profile.Fixture("limit-kills-4097");
        var rejected = Task2ReferenceOracle.Execute(profile, invalid);
        Assert.Equal(4097, rejected.RequestedInput.FutureKills);
        Assert.Equal(ArithmeticDisposition.ArithmeticLimitExceeded,
            rejected.ArithmeticDisposition);
        Assert.Equal(WaveDisposition.NoSafeRecommendation, rejected.WaveDisposition);
        Assert.Equal(0, rejected.ExecutedKills);
        Assert.Equal(0, rejected.TransitionCount);
        Console.WriteLine(
            $"TASK2_RELEASE_SURFACE validKills={bounty.ExecutedKills} mass={bounty.Mass} " +
            $"score={bounty.NavigationScore} invalidKills={rejected.RequestedInput.FutureKills} " +
            $"arithmetic={rejected.ArithmeticDisposition} wave={rejected.WaveDisposition}");
        var continuous = profile.Fixture("continuous-vector-6-12-24").SemanticExpected;
        var risk = profile.Fixture("risk-world-progression-22-100").SemanticExpected;
        var royal = profile.Fixture("royal-piecewise-proc-branches").SemanticExpected;
        Console.WriteLine(
            "TASK2_SEMANTIC_SURFACE continuous=6[random_wisp:1,lumber:1];" +
            "12[ship:1];24[ship:1] risk=" +
            string.Join(",", risk.RiskEffectiveSuccessPercents) +
            " royal=" + string.Join(",", royal.RoyalBranches.Select(branch =>
                $"{branch.Branch}:{branch.ProcDamage.Value}")));
        Assert.Equal([6, 12, 24],
            continuous.ContinuousRewards.Select(vector => vector.Attempt).ToArray());
    }

    [Fact]
    public void EveryExactLimitAndOneOverReturnsTheTypedUnclampedDisposition()
    {
        var profile = Load();
        var fixturePairs = new[]
        {
            ("limit-actions-64", "limit-actions-65"),
            ("limit-kills-4096", "limit-kills-4097"),
            ("limit-states-65536", "limit-states-65537"),
            ("limit-scenarios-4096", "limit-scenarios-4097"),
            ("limit-bits-262144", "limit-bits-262145")
        };

        foreach (var (atLimitId, oneOverId) in fixturePairs)
        {
            var atLimit = profile.Fixture(atLimitId);
            var allowed = Task2ReferenceOracle.Execute(profile, atLimit);
            AssertExpected(atLimit, allowed);
            Assert.Equal(ArithmeticDisposition.Allowed, allowed.ArithmeticDisposition);
            Assert.Equal(WaveDisposition.SafeRecommendation, allowed.WaveDisposition);

            var oneOver = profile.Fixture(oneOverId);
            var rejected = Task2ReferenceOracle.Execute(profile, oneOver);
            AssertExpected(oneOver, rejected);
            Assert.Equal(ArithmeticDisposition.ArithmeticLimitExceeded,
                rejected.ArithmeticDisposition);
            Assert.Equal(WaveDisposition.NoSafeRecommendation, rejected.WaveDisposition);
            Assert.Equal(oneOver.Input, rejected.RequestedInput);
            Assert.Equal(0, rejected.ExecutedActions);
            Assert.Equal(0, rejected.ExecutedKills);
            Assert.Equal(0, rejected.TerminalStateCount);
            Assert.Equal(0, rejected.ObservedScenarioCount);
            Assert.Equal(0, rejected.MaxObservedRationalBits);
        }

        AssertLimit(profile.Limits.CheckActions(64), 64, 64, true);
        AssertLimit(profile.Limits.CheckActions(65), 65, 64, false);
        AssertLimit(profile.Limits.CheckKills(4096), 4096, 4096, true);
        AssertLimit(profile.Limits.CheckKills(4097), 4097, 4096, false);
        AssertLimit(profile.Limits.CheckStates(65536), 65536, 65536, true);
        AssertLimit(profile.Limits.CheckStates(65537), 65537, 65536, false);
        AssertLimit(profile.Limits.CheckScenarios(4096), 4096, 4096, true);
        AssertLimit(profile.Limits.CheckScenarios(4097), 4097, 4096, false);
        AssertLimit(profile.Limits.CheckRationalBits(262144), 262144, 262144, true);
        AssertLimit(profile.Limits.CheckRationalBits(262145), 262145, 262144, false);
        Assert.Equal(ArithmeticDisposition.ArithmeticLimitExceeded,
            profile.Limits.CheckKills(-1).ArithmeticDisposition);
    }

    [Fact]
    [Trait("Task2Verifier", "Final")]
    public void AlchemyBindsDirectChildAndWrapperRuntimeImplementations()
    {
        var root = JsonNode.Parse(File.ReadAllText(ProfilePath()))!.AsObject();
        var ranges = root["Source"]!["Ranges"]!.AsArray();
        var direct = ranges.SingleOrDefault(node =>
            node!["Id"]!.GetValue<string>() == "alchemy-direct-child-runtime");
        var wrapper = ranges.SingleOrDefault(node =>
            node!["Id"]!.GetValue<string>() == "alchemy-decomposition");

        Assert.NotNull(direct);
        Assert.Equal("war3map.j", direct!["Artifact"]!.GetValue<string>());
        Assert.Equal("aKG", direct["Function"]!.GetValue<string>());
        Assert.Equal(69602, direct["StartLine"]!.GetValue<int>());
        Assert.Equal(69631, direct["EndLine"]!.GetValue<int>());
        Assert.Equal(69615, direct["EvidenceLine"]!.GetValue<int>());
        Assert.NotNull(wrapper);
        Assert.Equal("qIw", wrapper!["Function"]!.GetValue<string>());
        Assert.Equal(69632, wrapper["StartLine"]!.GetValue<int>());
        Assert.Equal(69662, wrapper["EndLine"]!.GetValue<int>());
        Assert.Equal(69658, wrapper["EvidenceLine"]!.GetValue<int>());
    }

    [Fact]
    [Trait("Task2Verifier", "Final")]
    public void PathRewardsAndTopRestrictionBindRuntimeLogicNotOnlyPresentation()
    {
        var root = JsonNode.Parse(File.ReadAllText(ProfilePath()))!.AsObject();
        var ranges = root["Source"]!["Ranges"]!.AsArray();
        var expected = new[]
        {
            ("path-top-restriction-runtime", "Elw", 13612, 13625, 13615),
            ("path-boss-1-reward-runtime", "LRE", 23056, 23088, 23077),
            ("path-boss-2-reward-runtime", "ScE", 42049, 42081, 42070),
            ("path-boss-3-reward-runtime", "pyN", 69217, 69249, 69238)
        };
        foreach (var (id, function, start, end, evidence) in expected)
        {
            var range = ranges.SingleOrDefault(node =>
                node!["Id"]!.GetValue<string>() == id);
            Assert.NotNull(range);
            Assert.Equal("war3map.j", range!["Artifact"]!.GetValue<string>());
            Assert.Equal(function, range["Function"]!.GetValue<string>());
            Assert.Equal(start, range["StartLine"]!.GetValue<int>());
            Assert.Equal(end, range["EndLine"]!.GetValue<int>());
            Assert.Equal(evidence, range["EvidenceLine"]!.GetValue<int>());
        }

        var bosses = root["PathBosses"]!.AsArray();
        Assert.Equal(3, bosses.Count);
        for (var index = 0; index < bosses.Count; index++)
        {
            Assert.Equal($"path-boss-{index + 1}-reward-runtime",
                bosses[index]!["RuntimeSourceRangeId"]!.GetValue<string>());
            Assert.Equal("navigation-definitions",
                bosses[index]!["PresentationSourceRangeIds"]![0]!.GetValue<string>());
        }
        var restriction = root["Transitions"]!.AsArray().Single(node =>
            node!["Id"]!.GetValue<string>() == "martial-top-restriction");
        Assert.Contains("path-top-restriction-runtime",
            restriction!["SourceRangeIds"]!.AsArray()
                .Select(node => node!.GetValue<string>()));
    }

    [Fact]
    [Trait("Task2Verifier", "Final")]
    public void ContinuousFixtureConsumesExactSixTwelveTwentyFourVectors()
    {
        AssertSemanticFixture("continuous-vector-6-12-24", "ContinuousSemantic",
            "Gambler.ContinuousBetting", "continuous-middle-gamble");
    }

    [Fact]
    [Trait("Task2Verifier", "Final")]
    public void RiskFixtureConsumesExactTwentyTwoThroughOneHundredProgression()
    {
        AssertSemanticFixture("risk-world-progression-22-100", "RiskProgression",
            "Gambler.RiskHedge", "risk-high-gamble");
    }

    [Fact]
    [Trait("Task2Verifier", "Final")]
    public void RoyalFixtureConsumesEveryExactPiecewiseProcBranch()
    {
        AssertSemanticFixture("royal-piecewise-proc-branches", "RoyalPiecewise",
            "PathOfKings.RoyalLoader", "royal-proc");
    }

    [Fact]
    [Trait("Task2Verifier", "Remaining")]
    public void PathTopRestrictionOwnsEveryExactRuntimeBranch()
    {
        var root = JsonNode.Parse(File.ReadAllText(ProfilePath()))!.AsObject();
        var expectedOwners = new[]
        {
            "PathOfKings.MartialLaw", "PathOfKings.BountyHunter",
            "PathOfKings.RoyalLoader"
        };
        var optionOwners = root["Options"]!.AsArray()
            .Where(option => option!["SourceRangeIds"]!.AsArray()
                .Any(source => source!.GetValue<string>() ==
                    "path-top-restriction-runtime"))
            .Select(option => option!["Id"]!.GetValue<string>()).ToArray();
        var bindingOwners = root["Bindings"]!.AsArray()
            .Where(binding => binding!["SourceRangeIds"]!.AsArray()
                .Any(source => source!.GetValue<string>() ==
                    "path-top-restriction-runtime"))
            .Select(binding => binding!["OptionId"]!.GetValue<string>()).ToArray();

        Assert.Equal(expectedOwners, optionOwners);
        Assert.Equal(expectedOwners, bindingOwners);
        var branches = root["Source"]!["Branches"]?.AsArray();
        Assert.NotNull(branches);
        Assert.Equal(2, branches!.Count);
        Assert.Equal(["PathOfKings.MartialLaw"],
            branches[0]!["OwnerOptionIds"]!.AsArray()
                .Select(value => value!.GetValue<string>()).ToArray());
        Assert.Equal(["PathOfKings.BountyHunter", "PathOfKings.RoyalLoader"],
            branches[1]!["OwnerOptionIds"]!.AsArray()
                .Select(value => value!.GetValue<string>()).ToArray());
    }

    [Theory]
    [Trait("Task2Verifier", "Remaining")]
    [InlineData("bounty-owner-toggle")]
    [InlineData("royal-owner-toggle")]
    [InlineData("extra-owner-toggle")]
    public void PathTopOptionSourceOwnershipMutationFailsBeforeBytePin(string mutation)
    {
        WithMutatedProfile(root =>
        {
            var optionId = mutation switch
            {
                "bounty-owner-toggle" => "PathOfKings.BountyHunter",
                "royal-owner-toggle" => "PathOfKings.RoyalLoader",
                _ => "AlliedForces.DoubleBenefit"
            };
            var option = root["Options"]!.AsArray().Single(value =>
                value!["Id"]!.GetValue<string>() == optionId)!;
            var binding = root["Bindings"]!.AsArray().Single(value =>
                value!["OptionId"]!.GetValue<string>() == optionId)!;
            ToggleSource(option["SourceRangeIds"]!.AsArray(),
                "path-top-restriction-runtime");
            ToggleSource(binding["SourceRangeIds"]!.AsArray(),
                "path-top-restriction-runtime");
        }, exception =>
            Assert.NotEqual("navigation profile bytes are not approved", exception.Message));
    }

    [Theory]
    [Trait("Task2Verifier", "Remaining")]
    [InlineData("owner-omission")]
    [InlineData("owner-extra")]
    [InlineData("owner-cross-swap")]
    [InlineData("wrong-branch-range")]
    [InlineData("wrong-branch-evidence")]
    public void PathTopBranchMutationFailsBeforeBytePin(string mutation)
    {
        WithMutatedProfile(root =>
        {
            var branches = root["Source"]!["Branches"]?.AsArray();
            Assert.NotNull(branches);
            var martial = branches![0]!;
            var oneTop = branches[1]!;
            switch (mutation)
            {
                case "owner-omission":
                    oneTop["OwnerOptionIds"]!.AsArray().RemoveAt(0);
                    break;
                case "owner-extra":
                    oneTop["OwnerOptionIds"]!.AsArray()
                        .Add("AlliedForces.DoubleBenefit");
                    break;
                case "owner-cross-swap":
                    martial["OwnerOptionIds"]![0] = "PathOfKings.BountyHunter";
                    break;
                case "wrong-branch-range":
                    oneTop["StartLine"] = 13618;
                    break;
                case "wrong-branch-evidence":
                    oneTop["EvidenceLine"] = 13620;
                    break;
            }
        }, exception =>
            Assert.NotEqual("navigation profile bytes are not approved", exception.Message));
    }

    [Fact]
    [Trait("Task2Verifier", "Remaining")]
    public void RoyalFixtureExecutesAttackSpeedTransformInsteadOfEchoingPoints()
    {
        var root = JsonNode.Parse(File.ReadAllText(ProfilePath()))!.AsObject();
        var jsonFixture = root["VerificationFixtures"]!.AsArray().Single(value =>
            value!["Id"]!.GetValue<string>() == "royal-piecewise-proc-branches")!;
        var scenarios = jsonFixture["SemanticInput"]!["RoyalAttackScenarios"]?.AsArray();
        Assert.NotNull(scenarios);
        Assert.Equal(4, scenarios!.Count);
        var expected = jsonFixture["SemanticExpected"]!["RoyalBranches"]!.AsArray();
        Assert.All(expected, branch =>
        {
            Assert.NotNull(branch!["ProcBaseWeaponCooldown"]);
            Assert.NotNull(branch["CooldownBefore"]);
            Assert.NotNull(branch["CooldownAfter"]);
            Assert.NotNull(branch["AttacksBefore"]);
            Assert.NotNull(branch["AttacksAfter"]);
            Assert.NotNull(branch["AttackCountDelta"]);
            Assert.Null(branch["AttackSpeedPoints"]);
        });

        var profile = Load();
        var fixture = profile.Fixture("royal-piecewise-proc-branches");
        var actual = Task2ReferenceOracle.Execute(profile, fixture);
        AssertExpected(fixture, actual);
        Assert.Equal([50, 39, 122, 8], actual.SemanticExpected.RoyalBranches
            .Select(branch => branch.AttacksBefore).ToArray());
        Assert.Equal([62, 46, 125, 10], actual.SemanticExpected.RoyalBranches
            .Select(branch => branch.AttacksAfter).ToArray());
        Assert.Equal([12, 7, 3, 2], actual.SemanticExpected.RoyalBranches
            .Select(branch => branch.AttackCountDelta).ToArray());
        for (var index = 0; index < fixture.SemanticInput.RoyalAttackScenarios.Length;
             index++)
        {
            Assert.Equal(
                fixture.SemanticInput.RoyalAttackScenarios[index].BaseWeaponCooldown.Value,
                actual.SemanticExpected.RoyalBranches[index]
                    .ProcBaseWeaponCooldown.Value);
        }

        var addedPoints = fixture.SemanticInput.RoyalAttackScenarios[1] with
        {
            AddedAttackSpeedPoints = 24
        };
        Assert.Throws<InvalidDataException>(() => Task2ReferenceOracle.Execute(profile,
            fixture with
            {
                SemanticInput = fixture.SemanticInput with
                {
                    RoyalAttackScenarios = fixture.SemanticInput.RoyalAttackScenarios
                        .SetItem(1, addedPoints)
                }
            }));
        var cap = fixture.SemanticInput.RoyalAttackScenarios[2] with
        {
            MaximumAttackSpeedPoints = 399
        };
        Assert.Throws<InvalidDataException>(() => Task2ReferenceOracle.Execute(profile,
            fixture with
            {
                SemanticInput = fixture.SemanticInput with
                {
                    RoyalAttackScenarios = fixture.SemanticInput.RoyalAttackScenarios
                        .SetItem(2, cap)
                }
            }));

        var changedBase = fixture.SemanticInput.RoyalAttackScenarios[0] with
        {
            BaseWeaponCooldown = new("21", "100")
        };
        var baseResult = Task2ReferenceOracle.Execute(profile, fixture with
        {
            SemanticInput = fixture.SemanticInput with
            {
                RoyalAttackScenarios = fixture.SemanticInput.RoyalAttackScenarios
                    .SetItem(0, changedBase)
            }
        });
        Assert.NotEqual(actual.SemanticExpected.RoyalBranches[0].ProcDamage.Value,
            baseResult.SemanticExpected.RoyalBranches[0].ProcDamage.Value);
        Assert.NotEqual(actual.SemanticExpected.RoyalBranches[0].CooldownAfter.Value,
            baseResult.SemanticExpected.RoyalBranches[0].CooldownAfter.Value);

        var changedHorizon = fixture.SemanticInput.RoyalAttackScenarios[0] with
        {
            EngagedHorizon = new("11", "1")
        };
        var horizonResult = Task2ReferenceOracle.Execute(profile, fixture with
        {
            SemanticInput = fixture.SemanticInput with
            {
                RoyalAttackScenarios = fixture.SemanticInput.RoyalAttackScenarios
                    .SetItem(0, changedHorizon)
            }
        });
        Assert.NotEqual(actual.SemanticExpected.RoyalBranches[0].AttacksAfter,
            horizonResult.SemanticExpected.RoyalBranches[0].AttacksAfter);

        var wrongBranch = fixture.SemanticExpected.RoyalBranches[0] with
        {
            AttacksAfter = 61
        };
        var wrongExpected = fixture with
        {
            SemanticExpected = fixture.SemanticExpected with
            {
                RoyalBranches = fixture.SemanticExpected.RoyalBranches
                    .SetItem(0, wrongBranch)
            }
        };
        Assert.Throws<Xunit.Sdk.EqualException>(() => AssertExpected(wrongExpected, actual));
    }

    [Theory]
    [Trait("Task2Verifier", "Remaining")]
    [InlineData("added-points")]
    [InlineData("cap")]
    [InlineData("base-cooldown")]
    [InlineData("horizon")]
    [InlineData("expected-attacks")]
    public void RoyalTransformMutationFailsBeforeBytePin(string mutation)
    {
        WithMutatedProfile(root =>
        {
            var fixture = root["VerificationFixtures"]!.AsArray().Single(value =>
                value!["Id"]!.GetValue<string>() ==
                    "royal-piecewise-proc-branches")!;
            var scenarios = fixture["SemanticInput"]!["RoyalAttackScenarios"]?.AsArray();
            Assert.NotNull(scenarios);
            switch (mutation)
            {
                case "added-points":
                    scenarios![1]!["AddedAttackSpeedPoints"] = 24;
                    break;
                case "cap":
                    scenarios![2]!["MaximumAttackSpeedPoints"] = 399;
                    break;
                case "base-cooldown":
                    scenarios![0]!["BaseWeaponCooldown"]!["Numerator"] = "21";
                    scenarios[0]!["BaseWeaponCooldown"]!["Denominator"] = "100";
                    break;
                case "horizon":
                    scenarios![0]!["EngagedHorizon"]!["Numerator"] = "11";
                    break;
                case "expected-attacks":
                    fixture["SemanticExpected"]!["RoyalBranches"]![0]!["AttacksAfter"] = 61;
                    break;
            }
        }, exception =>
            Assert.NotEqual("navigation profile bytes are not approved", exception.Message));
    }

    [Fact]
    [Trait("Task2Verifier", "Final")]
    public void FixturePoolCannotBelongToAnotherRealOption()
    {
        WithMutatedProfile(root =>
        {
            var fixture = root["VerificationFixtures"]!.AsArray().Single(node =>
                node!["Id"]!.GetValue<string>() == "pool-mass-royal-proc");
            fixture!["Input"]!["PoolId"] = "casino-normal";
        }, exception =>
            Assert.NotEqual("navigation profile bytes are not approved", exception.Message));
    }

    [Fact]
    public void PoolCannotBeReboundToAnotherValidOption()
    {
        WithMutatedProfile(root => Mutate(root, "pool-rebound"), exception =>
            Assert.NotEqual("navigation profile bytes are not approved", exception.Message));
    }

    [Theory]
    [InlineData("transition-rebound")]
    [InlineData("formula-rebound")]
    [InlineData("fixture-rebound")]
    [InlineData("fixture-transition-rebound")]
    [InlineData("fixture-formula-rebound")]
    [InlineData("fixture-source-rebound")]
    [InlineData("alchemy-runtime-source-missing")]
    [InlineData("path-runtime-source-missing")]
    [InlineData("path-runtime-evidence-range")]
    [InlineData("semantic-fixture-missing")]
    [InlineData("semantic-fixture-extra")]
    [InlineData("binding-rebound")]
    [InlineData("non-reduced-probability")]
    [InlineData("zero-probability")]
    [InlineData("probability-mass")]
    [InlineData("source-range")]
    [InlineData("source-member-hash")]
    [InlineData("option-top-range")]
    [InlineData("transition-parameter-range")]
    [InlineData("formula-non-reduced-probability")]
    [InlineData("fixture-non-reduced-rational")]
    [InlineData("basis-point-range")]
    public void NestedSemanticMutationFailsBeforeTheImmutableBytePin(string mutation)
    {
        WithMutatedProfile(root => Mutate(root, mutation), exception =>
            Assert.NotEqual("navigation profile bytes are not approved", exception.Message));
    }

    [Theory]
    [InlineData("null-pool-members")]
    [InlineData("null-member-probability")]
    [InlineData("null-transition-parameters")]
    [InlineData("null-formula-parameters")]
    [InlineData("null-fixture-expected")]
    [InlineData("null-binding-pools")]
    [InlineData("missing-pool-members")]
    [InlineData("nested-extra-property")]
    public void NestedNullMissingOrExtraFieldIsNormalizedToInvalidData(string mutation)
    {
        WithMutatedProfile(root => Mutate(root, mutation));
    }

    [Theory]
    [InlineData("duplicate-source-member")]
    [InlineData("duplicate-source-range")]
    [InlineData("duplicate-option")]
    [InlineData("duplicate-pool")]
    [InlineData("duplicate-pool-member")]
    [InlineData("duplicate-transition")]
    [InlineData("duplicate-transition-parameter")]
    [InlineData("duplicate-formula")]
    [InlineData("duplicate-fixture")]
    [InlineData("duplicate-binding")]
    public void DuplicateNestedIdentityFailsClosed(string mutation)
    {
        WithMutatedProfile(root => Mutate(root, mutation));
    }

    [Fact]
    public void DuplicateJsonPropertyAndAnyUnapprovedByteFailClosed()
    {
        var original = File.ReadAllText(ProfilePath());
        var duplicate = original.Replace(
            "\"Id\": \"allied-double\",",
            "\"Id\": \"allied-double\",\n      \"Id\": \"allied-double\",",
            StringComparison.Ordinal);
        Assert.NotEqual(original, duplicate);
        WithProfileText(duplicate, exception =>
            Assert.Contains("duplicate JSON property", exception.Message));
        WithProfileText(original + " ");
    }

    [Fact]
    public void MissingProfileIsNormalizedToInvalidData()
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "orand-navigation-" + Guid.NewGuid())).FullName;
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                NavigationMechanicsProfileLoader.LoadFromDirectory(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void AssertSemanticFixture(
        string id,
        string kind,
        string optionId,
        string poolId)
    {
        var root = JsonNode.Parse(File.ReadAllText(ProfilePath()))!.AsObject();
        var fixture = root["VerificationFixtures"]!.AsArray().SingleOrDefault(node =>
            node!["Id"]!.GetValue<string>() == id);
        Assert.NotNull(fixture);
        Assert.Equal(kind, fixture!["Kind"]!.GetValue<string>());
        Assert.Equal(optionId, fixture["OptionId"]!.GetValue<string>());
        Assert.Equal(poolId, fixture["Input"]!["PoolId"]!.GetValue<string>());

        var profile = Load();
        var typedFixture = profile.Fixture(id);
        var actual = Task2ReferenceOracle.Execute(profile, typedFixture);
        AssertExpected(typedFixture, actual);
    }

    private static NavigationMechanicsProfile Load() =>
        NavigationMechanicsProfileLoader.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Data"));

    private static string ProfilePath() =>
        Path.Combine(AppContext.BaseDirectory, "Data", "navigation-mechanics-2314.json");

    private static void AssertExpected(
        NavigationVerificationFixture fixture,
        OracleExecutionResult actual)
    {
        var expected = fixture.Expected;
        Assert.Equal(fixture.Input, actual.RequestedInput);
        Assert.Equal(expected.ArithmeticDisposition, actual.ArithmeticDisposition);
        Assert.Equal(expected.WaveDisposition, actual.WaveDisposition);
        Assert.Equal(OracleRational.Parse(expected.Mass), actual.Mass);
        Assert.Equal(OracleRational.Parse(expected.ExpectedValue), actual.ExpectedValue);
        Assert.Equal(OracleRational.Parse(expected.Variance), actual.Variance);
        Assert.Equal(expected.VarianceDenominatorBeforeReductionBits,
            actual.VarianceDenominatorBeforeReductionBits);
        Assert.Equal(expected.StandardDeviation, actual.StandardDeviation);
        Assert.Equal(expected.Quantile95, actual.Quantile95);
        Assert.Equal(expected.FloorBp, actual.FloorBp);
        Assert.Equal(expected.ValueBp, actual.ValueBp);
        Assert.Equal(expected.UpperBp, actual.UpperBp);
        Assert.Equal(expected.TopBp, actual.TopBp);
        Assert.Equal(expected.CaptureConflictBp, actual.CaptureConflictBp);
        Assert.Equal(expected.NavigationScore, actual.NavigationScore);
        Assert.Equal(expected.ExecutedActions, actual.ExecutedActions);
        Assert.Equal(expected.ExecutedKills, actual.ExecutedKills);
        Assert.Equal(expected.TerminalStateCount, actual.TerminalStateCount);
        Assert.Equal(expected.ObservedScenarioCount, actual.ObservedScenarioCount);
        Assert.Equal(expected.TransitionCount, actual.TransitionCount);
        Assert.Equal(expected.MaxObservedRationalBits, actual.MaxObservedRationalBits);
        AssertSemanticExpected(fixture.SemanticExpected, actual.SemanticExpected);
    }

    private static void AssertSemanticExpected(
        NavigationSemanticExpected expected,
        NavigationSemanticExpected actual)
    {
        Assert.Equal(expected.ContinuousRewards.Length, actual.ContinuousRewards.Length);
        for (var index = 0; index < expected.ContinuousRewards.Length; index++)
        {
            Assert.Equal(expected.ContinuousRewards[index].Attempt,
                actual.ContinuousRewards[index].Attempt);
            Assert.Equal(expected.ContinuousRewards[index].Rewards
                    .Select(reward => (reward.Id, reward.Count)),
                actual.ContinuousRewards[index].Rewards
                    .Select(reward => (reward.Id, reward.Count)));
        }
        Assert.Equal(expected.RiskRawThresholds.ToArray(),
            actual.RiskRawThresholds.ToArray());
        Assert.Equal(expected.RiskEffectiveSuccessPercents.ToArray(),
            actual.RiskEffectiveSuccessPercents.ToArray());
        Assert.Equal(expected.RoyalBranches.Length, actual.RoyalBranches.Length);
        for (var index = 0; index < expected.RoyalBranches.Length; index++)
        {
            var expectedBranch = expected.RoyalBranches[index];
            var actualBranch = actual.RoyalBranches[index];
            Assert.Equal(expectedBranch.Branch, actualBranch.Branch);
            Assert.Equal(expectedBranch.ProcDamage.Value, actualBranch.ProcDamage.Value);
            Assert.Equal(expectedBranch.ProcProbability.Value,
                actualBranch.ProcProbability.Value);
            Assert.Equal(expectedBranch.ProcCooldownInput,
                actualBranch.ProcCooldownInput);
            Assert.Equal(expectedBranch.ProcBaseWeaponCooldown.Value,
                actualBranch.ProcBaseWeaponCooldown.Value);
            Assert.Equal(expectedBranch.CooldownBefore.Value,
                actualBranch.CooldownBefore.Value);
            Assert.Equal(expectedBranch.CooldownAfter.Value,
                actualBranch.CooldownAfter.Value);
            Assert.Equal(expectedBranch.EngagedHorizon.Value,
                actualBranch.EngagedHorizon.Value);
            Assert.Equal(expectedBranch.AttacksBefore, actualBranch.AttacksBefore);
            Assert.Equal(expectedBranch.AttacksAfter, actualBranch.AttacksAfter);
            Assert.Equal(expectedBranch.AttackCountDelta, actualBranch.AttackCountDelta);
            Assert.Equal(expectedBranch.AttackDamage, actualBranch.AttackDamage);
            Assert.Equal(expectedBranch.Radius, actualBranch.Radius);
        }
    }

    private static void AssertLimit(
        ArithmeticCheck check,
        int requested,
        int limit,
        bool allowed)
    {
        Assert.Equal(requested, check.Requested);
        Assert.Equal(limit, check.Limit);
        Assert.Equal(allowed ? ArithmeticDisposition.Allowed :
            ArithmeticDisposition.ArithmeticLimitExceeded,
            check.ArithmeticDisposition);
        Assert.Equal(allowed ? WaveDisposition.SafeRecommendation :
            WaveDisposition.NoSafeRecommendation,
            check.WaveDisposition);
    }

    private static void ToggleSource(JsonArray sources, string id)
    {
        var existing = sources.SingleOrDefault(value => value!.GetValue<string>() == id);
        if (existing is null)
            sources.Add(id);
        else
            sources.Remove(existing);
    }

    private static void WithMutatedProfile(
        Action<JsonObject> mutate,
        Action<InvalidDataException>? inspect = null)
    {
        var root = JsonNode.Parse(File.ReadAllText(ProfilePath()))!.AsObject();
        mutate(root);
        WithProfileText(root.ToJsonString(new JsonSerializerOptions(
            JsonSerializerOptions.Default) { WriteIndented = true }), inspect);
    }

    private static void WithProfileText(
        string text,
        Action<InvalidDataException>? inspect = null)
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "orand-navigation-" + Guid.NewGuid())).FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "navigation-mechanics-2314.json"), text);
            var exception = Assert.Throws<InvalidDataException>(() =>
                NavigationMechanicsProfileLoader.LoadFromDirectory(directory));
            inspect?.Invoke(exception);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void Mutate(JsonObject root, string mutation)
    {
        var source = root["Source"]!.AsObject();
        var members = source["Members"]!.AsArray();
        var ranges = source["Ranges"]!.AsArray();
        var options = root["Options"]!.AsArray();
        var pools = root["Pools"]!.AsArray();
        var transitions = root["Transitions"]!.AsArray();
        var formulas = root["Formulas"]!.AsArray();
        var fixtures = root["VerificationFixtures"]!.AsArray();
        var bindings = root["Bindings"]!.AsArray();
        switch (mutation)
        {
            case "pool-rebound":
                pools[4]!["OptionId"] = "PathOfKings.RoyalLoader";
                break;
            case "transition-rebound":
                transitions[4]!["OptionId"] = "PathOfKings.RoyalLoader";
                break;
            case "formula-rebound":
                formulas[4]!["OptionId"] = "PathOfKings.RoyalLoader";
                break;
            case "fixture-rebound":
                fixtures[4]!["OptionId"] = "PathOfKings.RoyalLoader";
                break;
            case "fixture-transition-rebound":
                fixtures[5]!["TransitionId"] = "casino-sequential";
                break;
            case "fixture-formula-rebound":
                fixtures[5]!["FormulaId"] = "casino-formula";
                break;
            case "fixture-source-rebound":
                fixtures[5]!["SourceRangeIds"]![0] = "high-gamble";
                break;
            case "alchemy-runtime-source-missing":
                root["Alchemy"]!["SourceRangeIds"]!.AsArray().RemoveAt(0);
                break;
            case "path-runtime-source-missing":
                root["PathBosses"]![0]!["RuntimeSourceRangeId"] =
                    "path-boss-2-reward-runtime";
                break;
            case "path-runtime-evidence-range":
                ranges.Single(node => node!["Id"]!.GetValue<string>() ==
                    "path-top-restriction-runtime")!["EvidenceLine"] = 13626;
                break;
            case "semantic-fixture-missing":
            {
                var value = fixtures.Single(node => node!["Id"]!.GetValue<string>() ==
                    "continuous-vector-6-12-24");
                fixtures.Remove(value);
                break;
            }
            case "semantic-fixture-extra":
            {
                var value = fixtures.Single(node => node!["Id"]!.GetValue<string>() ==
                    "continuous-vector-6-12-24")!.DeepClone();
                value!["Id"] = "continuous-vector-extra";
                fixtures.Add(value);
                break;
            }
            case "binding-rebound":
                bindings[4]!["PoolIds"]![0] = "royal-proc";
                break;
            case "non-reduced-probability":
                pools[5]!["Members"]![0]!["Probability"]!["Numerator"] = "4";
                pools[5]!["Members"]![0]!["Probability"]!["Denominator"] = "50";
                break;
            case "zero-probability":
                pools[5]!["Members"]![0]!["Probability"]!["Numerator"] = "0";
                break;
            case "probability-mass":
                pools[5]!["Members"]![1]!["Probability"]!["Numerator"] = "22";
                break;
            case "source-range":
                ranges[8]!["EndLine"] = 88594;
                break;
            case "source-member-hash":
                members[1]!["Sha256"] = "stale";
                break;
            case "option-top-range":
                options[0]!["TopUnitLimit"] = 2;
                break;
            case "transition-parameter-range":
                transitions[0]!["Integers"]![0]!["Value"] = -1;
                break;
            case "formula-non-reduced-probability":
                formulas[5]!["Probabilities"]![0]!["Value"]!["Numerator"] = "4";
                formulas[5]!["Probabilities"]![0]!["Value"]!["Denominator"] = "50";
                break;
            case "fixture-non-reduced-rational":
                fixtures[0]!["Expected"]!["Mass"]!["Numerator"] = "2";
                fixtures[0]!["Expected"]!["Mass"]!["Denominator"] = "2";
                break;
            case "basis-point-range":
                fixtures[16]!["Input"]!["CoreFloorBp"] = 10001;
                break;
            case "null-pool-members":
                pools[0]!["Members"] = null;
                break;
            case "null-member-probability":
                pools[0]!["Members"]![0]!["Probability"] = null;
                break;
            case "null-transition-parameters":
                transitions[0]!["Integers"] = null;
                break;
            case "null-formula-parameters":
                formulas[0]!["Probabilities"] = null;
                break;
            case "null-fixture-expected":
                fixtures[0]!["Expected"] = null;
                break;
            case "null-binding-pools":
                bindings[0]!["PoolIds"] = null;
                break;
            case "missing-pool-members":
                pools[0]!.AsObject().Remove("Members");
                break;
            case "nested-extra-property":
                pools[0]!["Unexpected"] = 1;
                break;
            case "duplicate-source-member":
                members[1]!["Name"] = members[0]!["Name"]!.GetValue<string>();
                break;
            case "duplicate-source-range":
                ranges[1]!["Id"] = ranges[0]!["Id"]!.GetValue<string>();
                break;
            case "duplicate-option":
                options[1]!["Id"] = options[0]!["Id"]!.GetValue<string>();
                break;
            case "duplicate-pool":
                pools[1]!["Id"] = pools[0]!["Id"]!.GetValue<string>();
                break;
            case "duplicate-pool-member":
                pools[4]!["Members"]![1]!["Id"] =
                    pools[4]!["Members"]![0]!["Id"]!.GetValue<string>();
                break;
            case "duplicate-transition":
                transitions[1]!["Id"] = transitions[0]!["Id"]!.GetValue<string>();
                break;
            case "duplicate-transition-parameter":
                transitions[2]!["Integers"]![1]!["Name"] =
                    transitions[2]!["Integers"]![0]!["Name"]!.GetValue<string>();
                break;
            case "duplicate-formula":
                formulas[1]!["Id"] = formulas[0]!["Id"]!.GetValue<string>();
                break;
            case "duplicate-fixture":
                fixtures[1]!["Id"] = fixtures[0]!["Id"]!.GetValue<string>();
                break;
            case "duplicate-binding":
                bindings[1]!["OptionId"] = bindings[0]!["OptionId"]!.GetValue<string>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }
}
