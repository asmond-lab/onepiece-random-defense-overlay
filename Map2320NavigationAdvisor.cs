using System;
using System.Collections.Generic;
using System.Linq;

namespace OrandOverlay;

public sealed record Map2320NavigationContext(Map2320Mode Mode, int? Wave, int? TopUnits, bool StateComplete);
public sealed record Map2320NavigationPresentation(string Id,string Name,string Category,string Summary,string Status,bool IsSafeRecommendation,IReadOnlyList<string> SourceFunctions);
public sealed record Map2320NavigationAdvice(string MapVersion,string Status,IReadOnlyList<Map2320NavigationPresentation> Options,IReadOnlyList<string> Limitations)
{
    public string? RecommendedOptionId => null;
}
/// <summary>Presentation only, deliberately disconnected from the legacy probability engine.</summary>
public static class Map2320NavigationAdvisor
{
    public static Map2320NavigationAdvice Advise(Map2320NavigationData data,Map2320NavigationContext context)
    {
        if(data.MapVersion!=Map2320NavigationMechanics.MapVersion || data.SchemaVersion!=1) throw new ArgumentException("Requires source-pinned 2.320 data.",nameof(data));
        bool known=context.Mode is Map2320Mode.Normal or Map2320Mode.Otherworld && context.Wave is >=0 && context.TopUnits is >=0 && context.StateComplete;
        var options=data.Options.Select(o=>{
            string status="Unknown: scoring and RNG distribution not migrated";
            if(!known) status="Unknown: mode, wave, or state incomplete";
            else if(o.Category=="PathOfKings" && Map2320NavigationMechanics.PathSelection(o.Id=="PathOfKings.MartialLaw",context.TopUnits)==Map2320Status.Ineligible) status="Ineligible: top-unit restriction";
            return new Map2320NavigationPresentation(o.Id,o.Name,o.Category,string.Join(" ",o.Effects),status,false,Array.AsReadOnly(o.SourceFunctions));
        }).ToArray();
        return new("2.320","Unknown: bounded mechanics available; no safe ranking",Array.AsReadOnly(options),Array.AsReadOnly(data.Limitations));
    }
    public static Map2320NavigationAdvice LoadAndAdvise(string dataPath,Map2320NavigationContext context) => Advise(Map2320NavigationMechanics.Load(dataPath),context);
}
