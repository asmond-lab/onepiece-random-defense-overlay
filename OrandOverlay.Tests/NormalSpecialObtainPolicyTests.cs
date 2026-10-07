using Xunit;

namespace OrandOverlay.Tests;

public sealed class NormalSpecialObtainPolicyTests
{
    private static UnitDefinition U(string id, string name, string tier) => new()
    {
        Id = id, Name = name, Tier = tier
    };

    [Fact]
    public void RayleighStaysWhenAnyObtainPathRemains()
    {
        var rayleigh = U(NormalSpecialObtainPolicy.RayleighLegend, "레일리", "전설");
        Assert.False(NormalSpecialObtainPolicy.HideCandidate(rayleigh,
            new Dictionary<string, int> { [NormalSpecialObtainPolicy.TranscendenceWisp] = 1 }));
        Assert.False(NormalSpecialObtainPolicy.HideCandidate(rayleigh,
            new Dictionary<string, int> { [NormalSpecialObtainPolicy.AncientShip] = 1 }));
        Assert.False(NormalSpecialObtainPolicy.HideCandidate(rayleigh,
            new Dictionary<string, int> { [NormalSpecialObtainPolicy.RareWisp] = 1 }));
        Assert.True(NormalSpecialObtainPolicy.HideCandidate(rayleigh, new Dictionary<string, int>()));
    }

    [Fact]
    public void TranscendenceHidesAfterWispSpentAwayFromKuma()
    {
        var unit = U("rawcode:E90H", "도플라밍고", "초월");
        Assert.True(NormalSpecialObtainPolicy.HideCandidate(unit, new Dictionary<string, int>()));
        Assert.True(NormalSpecialObtainPolicy.HideCandidate(unit,
            new Dictionary<string, int> { [NormalSpecialObtainPolicy.RayleighRare] = 1 }));
        Assert.False(NormalSpecialObtainPolicy.HideCandidate(unit,
            new Dictionary<string, int> { [NormalSpecialObtainPolicy.TranscendenceWisp] = 1 }));
        Assert.False(NormalSpecialObtainPolicy.HideCandidate(unit,
            new Dictionary<string, int> { [NormalSpecialObtainPolicy.TranscendenceKuma] = 1 }));
    }

    }
