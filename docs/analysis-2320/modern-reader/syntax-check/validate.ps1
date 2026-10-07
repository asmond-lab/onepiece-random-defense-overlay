$ErrorActionPreference='Stop'
$work=$PSScriptRoot
foreach($p in @('pidgin\lib\net5.0\Pidgin.dll','war3net.common\lib\net6.0\War3Net.Common.dll','war3net.codeanalysis\lib\net6.0\War3Net.CodeAnalysis.dll','parser\lib\net6.0\War3Net.CodeAnalysis.Jass.dll')){[Reflection.Assembly]::LoadFrom("$work\$p") | Out-Null}
$base=Split-Path (Split-Path $work)
$results=@()
foreach($file in @("$base\modern-reader\members-ko-2.320\war3map.j","$base\indexed-members\2314\war3map.j")){
 $bytes=[IO.File]::ReadAllBytes($file); $text=[Text.UTF8Encoding]::new($false,$true).GetString($bytes)
 $r=[ordered]@{file=$file;bytes=$bytes.Length;sha256=(Get-FileHash $file -Algorithm SHA256).Hash;utf16Length=$text.Length;strictUTF8=$true;parser='War3Net.CodeAnalysis.Jass 6.0.2';status='INCONCLUSIVE'}
 $timer=[Diagnostics.Stopwatch]::StartNew()
 try{
  $tree=[War3Net.CodeAnalysis.Jass.JassSyntaxFactory]::ParseCompilationUnit($text)
  $r.status='PASS'; $r.declarations=$tree.Declarations.Length; $r.topLevelTypes=@($tree.Declarations | Group-Object {$_.GetType().Name} | Select-Object Name,Count); $r.eofToken=$tree.EndOfFileToken.ToString()
  $tree.GetType().GetMethods() | Where-Object {$_.Name -match 'Child|Descend|Write|String'} | ForEach-Object {$_.ToString()}
  $tree.Declarations | Select-Object -First 2 | ForEach-Object {$_.GetType().FullName; $_.GetType().GetProperties() | ForEach-Object {$_.ToString()}}
 }catch{
  $ex=$_.Exception; while($ex.InnerException){$ex=$ex.InnerException}; $r.status=if($ex -is [Pidgin.ParseException]){'FAIL'}else{'INCONCLUSIVE'}; $r.errorType=$ex.GetType().FullName; $r.error=$ex.Message; $r.errorDetail=$ex.ToString()
 }
 $timer.Stop();$r.elapsedSeconds=$timer.Elapsed.TotalSeconds;$results+=[pscustomobject]$r
}
$results | ConvertTo-Json -Depth 8 | Set-Content "$work\initial-results.json"
$results | ConvertTo-Json -Depth 8
# Reject trailing garbage to verify complete-input entry point.
try{[War3Net.CodeAnalysis.Jass.JassSyntaxFactory]::ParseCompilationUnit("function syntax_probe takes nothing returns nothing`nendfunction`n@@@") | Out-Null; 'TRAILING-GARBAGE-CONTROL: UNEXPECTED ACCEPT'}catch{'TRAILING-GARBAGE-CONTROL: REJECTED '+$_.Exception.Message}
$meta=@()
foreach($r in $results){
 $text=[Text.UTF8Encoding]::new($false,$true).GetString([IO.File]::ReadAllBytes($r.file))
 $tree=[War3Net.CodeAnalysis.Jass.JassSyntaxFactory]::ParseCompilationUnit($text)
 $nodes=@($tree.GetDescendantNodes());$counts=@($nodes | Group-Object {$_.GetType().Name} | Sort-Object Name | Select-Object Name,Count)
 $globals=@($tree.Declarations | Where-Object {$_.GetType().Name -eq 'JassGlobalsDeclarationSyntax'} | ForEach-Object {$_.GlobalDeclarations})
 $m=[ordered]@{file=$r.file;status='PASS';errors=@();errorLines=@();errorOffsets=@();fullStringExactlyEqualsInput=($tree.ToFullString() -ceq $text);sha256After=(Get-FileHash $r.file -Algorithm SHA256).Hash;globalDeclarationCount=$globals.Count;astNodeCounts=$counts;globalTypeCounts=@($globals | Group-Object {$_.GetType().Name} | Select-Object Name,Count)}
 $meta+=[pscustomobject]$m
 $globals | Select-Object -First 1 | ForEach-Object {$_.GetType().GetProperties() | ForEach-Object {$_.ToString()}}
}
$meta | ConvertTo-Json -Depth 8 | Set-Content "$work\ast-metadata.json"
$meta | ConvertTo-Json -Depth 8
$manifest=[ordered]@{parser='War3Net.CodeAnalysis.Jass';version='6.0.2';repositoryCommit='48ccced0e7bbf81595505092b62226b480349467';entryPoint='JassSyntaxFactory.ParseCompilationUnit(string)';host=[Runtime.InteropServices.RuntimeInformation]::FrameworkDescription;packageHashes=@(Get-ChildItem $work -Filter '*.nupkg' | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash);assemblies=@([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {$_.GetName().Name -match '^(War3Net|Pidgin|System.Collections.Immutable|System.Runtime.CompilerServices.Unsafe)'} | ForEach-Object {[ordered]@{name=$_.FullName;location=$_.Location}});negativeControl='Valid function followed by @@@ rejected at line 3 col 1';note='No source/map/game execution. Input files read only. Direct .NET assembly load after NuGet restore failed. Version 6.0.3 not published for this package; pinned published 6.0.2.'}
$manifest | ConvertTo-Json -Depth 8 | Set-Content "$work\parser-manifest.json"
