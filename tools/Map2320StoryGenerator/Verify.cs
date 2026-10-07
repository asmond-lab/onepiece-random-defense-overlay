using System;
using System.IO;
using System.Linq;
using System.Collections.Immutable;
using OrandOverlay;
class Verify {
 static void Main(string[] args) {
 var p=Map2320StoryProfile.LoadFromDirectory(args[0]);
 if(p.Stages.Length!=14 || p.ProjectGuaranteedBaseStages().Length!=14)throw new Exception();
 foreach(var s in p.Stages.Take(3)) if(s.Qualify(true,null,null,null)!=Map2320QualificationResult.Qualified||s.ContributionQualified.Length!=0||s.Mvp.Length!=2)throw new Exception();
 for(int n=0;n<=4;n++){var players=Enumerable.Range(0,4).Select(i=>new Map2320PlayerAtCompletion(i<n,true,true)).ToArray();int threshold=n==2?30:n==3?25:20;
 if(p.Stages[3].Qualify(true,threshold,100,players)!=Map2320QualificationResult.Qualified || p.Stages[3].Qualify(true,threshold-.01,100,players)!=Map2320QualificationResult.NotQualified)throw new Exception();}
 if(p.Stages[3].Qualify(true,null,100,null)!=Map2320QualificationResult.Unknown)throw new Exception();
 foreach(var s in p.Stages) foreach(var group in new[]{s.EveryPlayerBase,s.ContributionQualified,s.Mvp,s.HiddenOrSideEffect}) foreach(var r in group) if(r.Sources.IsDefaultOrEmpty)throw new Exception();
 var text=File.ReadAllText(Path.Combine(args[0],"story-progression-2320.json"));
 foreach(var changed in new[]{text.Replace("\"SchemaVersion\": 1","\"SchemaVersion\": 1, \"SchemaVersion\": 1"),text.Replace("\"SchemaVersion\": 1","\"SchemaVersion\": 1, \"Unknown\": 0"),text.Replace("\"Amount\": 180","\"Amount\": 181"),text.Replace("\"Id\": \"e01A\"","\"Id\": \"e0IA\"")}){try{Map2320StoryProfile.Load(System.Text.Encoding.UTF8.GetBytes(changed));throw new Exception("accepted mutation");}catch(InvalidDataException){}}
 Console.WriteLine("PASS: 14 stages, all reward source groups, projection, first3/MVP separation, 5 population boundaries, unknown, duplicate/unknown/amount/dummy mutation rejection. SHA256="+Map2320StoryProfile.DataSha256);
 }
}
