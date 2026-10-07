namespace OrandOverlay;

/// <summary>Explicit observations only. No native reader or inventory inference is activated.</summary>
public sealed record RecipeConditionObservation(string MapVersion, string SessionId, int OwnerId,
    DateTimeOffset ObservedAt, int? NikaTraitFlag = null,
    IReadOnlyDictionary<string, bool>? Tokens = null, int? Lumber = null)
{
    public long? MatchGeneration { get; init; }
    public long? RecognitionRevision { get; init; }
}

public sealed record RecipeConditionContext(string MapVersion, string SessionId, int OwnerId,
    DateTimeOffset Now, RecipeConditionObservation? Observation = null, TimeSpan? MaximumAge = null)
{
    public long? MatchGeneration { get; init; }
    public long? RecognitionRevision { get; init; }
}

public enum RecipeConditionStatus { Unknown, Blocked, Satisfied }
public sealed record RecipeConditionResult(RecipeConditionStatus Status, string Reason)
{
    public bool IsSatisfied => Status == RecipeConditionStatus.Satisfied;
}
public sealed record RecipeConditionRequirements(string MapVersion, bool RequiresNikaTraitFlag,
    string? TokenId, int TokenCount, int Lumber)
{
    public string? UnresolvedSourceConditions { get; init; }
}
