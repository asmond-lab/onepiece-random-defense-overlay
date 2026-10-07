namespace OrandOverlay;

public static class RecipeConditionEvaluator
{
    /// <summary>Only current-frame evidence can authorize coach recipe instructions.</summary>
    public static RecipeConditionContext? BindToFrame(RecipeConditionContext? context,
        long matchGeneration, long recognitionRevision, bool isCurrent)
    {
        var observation = context?.Observation;
        return isCurrent && matchGeneration > 0 && recognitionRevision > 0 &&
            context?.MatchGeneration == matchGeneration && context.RecognitionRevision == recognitionRevision &&
            observation?.MatchGeneration == matchGeneration && observation.RecognitionRevision == recognitionRevision
            ? context : null;
    }

    public static RecipeConditionResult Evaluate(UnitDefinition unit, RecipeConditionContext? context = null) =>
        Evaluate(unit.RecipeConditions, context);

    public static RecipeConditionResult Evaluate(RecipeConditionRequirements? required,
        RecipeConditionContext? context = null)
    {
        if (required is null) return new(RecipeConditionStatus.Satisfied, "");
        if (!string.IsNullOrEmpty(required.UnresolvedSourceConditions))
            return new(RecipeConditionStatus.Unknown, "조합 보류: 미해결 원본 조건 · " + required.UnresolvedSourceConditions);
        var observation = context?.Observation;
        if (context is null || observation is null || string.IsNullOrWhiteSpace(context.SessionId) ||
            context.OwnerId < 0 || context.MapVersion != required.MapVersion ||
            observation.MapVersion != context.MapVersion || observation.SessionId != context.SessionId ||
            observation.OwnerId != context.OwnerId ||
            ((context.MatchGeneration is not null || context.RecognitionRevision is not null ||
              observation.MatchGeneration is not null || observation.RecognitionRevision is not null) &&
             (context.MatchGeneration is not > 0 || context.RecognitionRevision is not > 0 ||
              observation.MatchGeneration != context.MatchGeneration || observation.RecognitionRevision != context.RecognitionRevision)) ||
            observation.ObservedAt > context.Now ||
            context.Now - observation.ObservedAt > (context.MaximumAge ?? TimeSpan.FromSeconds(5)))
            return new(RecipeConditionStatus.Unknown, "조합 보류: 현재 맵·세션·소유자의 조건 관측이 필요합니다.");
        if (required.RequiresNikaTraitFlag && observation.NikaTraitFlag == 0)
            return new(RecipeConditionStatus.Blocked, "조합 보류: 특성강화가 필요합니다.");
        // JASS ITEM has boolean capacity 1, never an item stack or charge count.
        if (required.TokenCount > 1 || (required.TokenCount > 0 && required.TokenId is { } token &&
            observation.Tokens?.TryGetValue(token, out var present) == true && !present))
            return new(RecipeConditionStatus.Blocked, "조합 보류: 플레이어 토큰 조건이 충족되지 않았습니다.");
        if (observation.Lumber is { } wood && wood < required.Lumber)
            return new(RecipeConditionStatus.Blocked, "조합 보류: 목재가 부족합니다.");
        if ((required.RequiresNikaTraitFlag && observation.NikaTraitFlag is null) ||
            (required.TokenCount > 0 && (required.TokenId is null || observation.Tokens is null ||
                !observation.Tokens.ContainsKey(required.TokenId))) ||
            (required.Lumber > 0 && observation.Lumber is null))
            return new(RecipeConditionStatus.Unknown, "조합 보류: 특성강화·플레이어 토큰·목재 조건 확인이 필요합니다.");
        return new(RecipeConditionStatus.Satisfied, "현재 조건 확인됨");
    }
}
