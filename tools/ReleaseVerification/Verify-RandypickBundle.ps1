param(
 [Parameter(Mandatory)][string]$Exe,
 [Parameter(Mandatory)][string]$Manifest,
 [Parameter(Mandatory)][string]$Version,
 [Parameter(Mandatory)][string]$OutputDirectory,
 [string]$SourceRoot=([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')))
)
$ErrorActionPreference='Stop'
$base=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $base){throw 'Use a new verification directory to avoid stale evidence.'}
[IO.Directory]::CreateDirectory($base)|Out-Null
$exeName=[IO.Path]::GetFileName($Exe)
if($exeName -cne 'RandyPick.exe'){throw 'Expected canonical published filename RandyPick.exe'}
$bytes=[IO.File]::ReadAllBytes([IO.Path]::GetFullPath($Exe))
$exeHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
$marker=[byte[]](0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae)
$position=[Text.Encoding]::Latin1.GetString($bytes).IndexOf([Text.Encoding]::Latin1.GetString($marker),[StringComparison]::Ordinal)
if($position -lt 8){throw 'No valid .NET single-file bundle marker'}
$header=[BitConverter]::ToInt64($bytes,$position-8)
if($header -lt 0 -or $header -ge $bytes.LongLength){throw 'Bundle header out of bounds'}
$stream=[IO.MemoryStream]::new($bytes,$false);$reader=[IO.BinaryReader]::new($stream)
$stream.Position=$header
$major=$reader.ReadUInt32();$minor=$reader.ReadUInt32();$count=$reader.ReadInt32();$id=$reader.ReadString()
if($major -ne 6 -or $count -lt 50 -or $count -gt 10000){throw "Unexpected bundle header $major.$minor / $count"}
$null=$reader.ReadInt64();$null=$reader.ReadInt64();$null=$reader.ReadInt64();$null=$reader.ReadInt64();$null=$reader.ReadUInt64()
$entries=[Collections.Generic.List[object]]::new();$names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$totalSize=0L
for($i=0;$i -lt $count;$i++){
 $offset=$reader.ReadInt64();$size=$reader.ReadInt64();$compressed=$reader.ReadInt64();$type=$reader.ReadByte();$name=$reader.ReadString().Replace('\','/')
 if([IO.Path]::IsPathRooted($name) -or $name -match '(^|/)\.\.(/|$)|:' -or -not $names.Add($name)){throw "Unsafe or duplicate bundle path: $name"}
 if($name -match '(?i)(^|/)(settings\.json|pending|telemetry|gameplay-v3|credentials?|secrets?|\.git)(/|$)|\.(pdb|log|cs|dpapi|pfx|pem|key)$'){throw "Unexpected private/development entry: $name"}
 $stored=if($compressed -gt 0){$compressed}else{$size}
 if($offset -lt 0 -or $size -lt 0 -or $compressed -lt 0 -or $size -gt 512MB -or $stored -gt $bytes.LongLength -or $offset -gt ($bytes.LongLength-$stored)){throw "Invalid bounds: $name"}
 $totalSize+=$size;if($totalSize -gt 2GB){throw 'Bundle expanded-size limit exceeded'}
 $entries.Add([pscustomobject]@{Path=$name;Offset=$offset;Size=$size;CompressedSize=$compressed;Type=$type;Sha256=''})
}
$reader.Dispose();$stream.Dispose()
$extract=Join-Path $base 'extracted';[IO.Directory]::CreateDirectory($extract)|Out-Null
# Validate every payload; persist only the application, runtime metadata and Data.
foreach($entry in $entries){
 $stored=[int]$(if($entry.CompressedSize -gt 0){$entry.CompressedSize}else{$entry.Size})
 $payload=[byte[]]::new($stored);[Array]::Copy($bytes,$entry.Offset,$payload,0,$stored)
 if($entry.CompressedSize -gt 0){
  $input=[IO.MemoryStream]::new($payload,$false);$deflate=[IO.Compression.DeflateStream]::new($input,[IO.Compression.CompressionMode]::Decompress);$decoded=[IO.MemoryStream]::new()
  try{$buffer=[byte[]]::new(65536);while(($read=$deflate.Read($buffer,0,$buffer.Length)) -gt 0){if($decoded.Length+$read -gt $entry.Size){throw "Oversized decompressed payload $($entry.Path)"};$decoded.Write($buffer,0,$read)};$payload=$decoded.ToArray()}finally{$decoded.Dispose();$deflate.Dispose();$input.Dispose()}
 }
 if($payload.LongLength -ne $entry.Size){throw "Decoded length mismatch: $($entry.Path)"}
 $entry.Sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($payload))
 if($entry.Path.StartsWith('Data/',[StringComparison]::Ordinal) -or $entry.Path -in @('OrandOverlay.dll','OrandOverlay.runtimeconfig.json')){
  $destination=Join-Path $extract $entry.Path;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))|Out-Null;[IO.File]::WriteAllBytes($destination,$payload)
 }
}
foreach($required in @('OrandOverlay.dll','OrandOverlay.runtimeconfig.json','System.Private.CoreLib.dll','System.Runtime.dll','wpfgfx_cor3.dll','Data/randypick-utility-48129.json','Data/randypick-normal-guide-48129.json','Data/map-combine-hotkeys-2320.json')){if(-not $names.Contains($required)){throw "Missing required entry $required"}}
$runtime=Get-Content -LiteralPath (Join-Path $extract 'OrandOverlay.runtimeconfig.json') -Raw|ConvertFrom-Json
if($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks){throw 'Installed framework unexpectedly required'}
foreach($framework in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')){if($runtime.runtimeOptions.includedFrameworks.name -notcontains $framework){throw "Missing included framework $framework"}}
$dataCount=0
foreach($entry in $entries|Where-Object {$_.Path.StartsWith('Data/',[StringComparison]::Ordinal)}){
 $source=Join-Path $SourceRoot $entry.Path;if($entry.Path -eq 'Data/bullet-guide-1-source.md'){$source=Join-Path $SourceRoot 'docs/bullet-guide-1-source.md'}
 if(-not [IO.File]::Exists($source)){throw "Unknown packaged data: $($entry.Path)"}
 if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne $entry.Sha256){throw "Packaged data mismatch: $($entry.Path)"};$dataCount++
}
if($dataCount -ne 332){throw "Expected 332 packaged Data files, found $dataCount"}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $extract 'OrandOverlay.dll'))
$update=$assembly.GetType('OrandOverlay.UpdateService',$true)
$isTestBuild=$update.GetProperty('IsTestBuild').GetValue($null)
$autoUpdateChannel=$update.GetProperty('SelectedChannel').GetValue($null)
$autoUpdateManifestUrl=$update.GetProperty('SelectedManifestUrl').GetValue($null)
$supportsAutomaticUpdates=$update.GetProperty('SupportsAutomaticUpdates').GetValue($null)
$expectedManifestUrl='https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64'
if(-not $isTestBuild -or $autoUpdateChannel -cne 'test' -or $autoUpdateManifestUrl -cne $expectedManifestUrl -or -not $supportsAutomaticUpdates){throw 'Packaged test-channel automatic-update contract differs from the expected enabled test feed'}
if($update.GetProperty('CurrentBuildVersion').GetValue($null) -cne $Version){throw 'Wrong packaged application version'}
if([Diagnostics.FileVersionInfo]::GetVersionInfo($Exe).ProductVersion -cne $Version){throw 'Wrong executable ProductVersion'}
$verified=$assembly.GetType('OrandOverlay.SignedUpdateManifest',$true).GetMethod('Verify',[Reflection.BindingFlags]'Public,Static').Invoke($null,@([IO.File]::ReadAllText($Manifest),'application','test'))
$expectedUrl="https://orand-updates.epic42121.workers.dev/downloads/$Version/RandyPick.exe"
if($verified.Kind -cne 'application' -or $verified.Channel -cne 'test' -or $verified.Platform -cne 'win-x64' -or $verified.AssetName -cne 'RandyPick.exe' -or $verified.Version -cne $Version -or $verified.AssetSize -ne $bytes.LongLength -or $verified.Sha256 -ine $exeHash -or $verified.DownloadUrl.ToString() -cne $expectedUrl){throw 'Signed envelope differs from the real executable or expected release route'}
$bundle=$assembly.GetType('OrandOverlay.Map2320DataBundle',$true).GetMethod('LoadFromDirectory').Invoke($null,[object[]]@([string](Join-Path $extract 'Data')))
if($bundle.LiveRecognitionSupported -or $bundle.AutomaticNavigationScoringSupported){throw 'Offline approval scope changed'}
$profiles=Get-Content -LiteralPath (Join-Path $extract 'Data/memory-profiles.json') -Raw|ConvertFrom-Json -NoEnumerate
& (Join-Path $PSScriptRoot 'Assert-RandypickReferenceProfiles.ps1') -Profiles $profiles
$resourceStream=$assembly.GetManifestResourceStream('OrandOverlay.g.resources')
if($null -eq $resourceStream){throw 'Missing WPF resources'}
$resources=[Resources.ResourceReader]::new($resourceStream)
try{$keys=@();$enumerator=$resources.GetEnumerator();while($enumerator.MoveNext()){$keys+=[string]$enumerator.Key};foreach($logo in @('assets/randypick-logo-64.png','assets/randypick-logo-256.png')){if($keys -notcontains $logo){throw "Missing packaged logo $logo"}}}finally{$resources.Dispose();$resourceStream.Dispose()}
foreach($requiredType in @('GameplayReceiptMatcher','GameplayRecoveryJournal','GameplayTelemetryCheckpoint')){if(-not $assembly.GetType('OrandOverlay.'+$requiredType)){throw "Missing retained reliability type $requiredType"}}
$entries|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $base 'bundle-manifest.json') -Encoding utf8
[pscustomobject]@{Version=$Version;FileName=$exeName;ManifestAssetName=$verified.AssetName;IsTestBuild=$isTestBuild;AutoUpdateChannel=$autoUpdateChannel;AutoUpdateManifestUrl=$autoUpdateManifestUrl;SupportsAutomaticUpdates=$supportsAutomaticUpdates;Entries=$count;DecodedEntries=$count;DataFiles=$dataCount;Size=$bytes.LongLength;Sha256=$exeHash;SignatureVerified=$true;SignedAssetMatches=$true;LogoResourcesVerified=$true;Fingerprint=$bundle.Fingerprint;LiveRecognitionSupported=$bundle.LiveRecognitionSupported;AutomaticNavigationScoringSupported=$bundle.AutomaticNavigationScoringSupported;Published=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $base 'package-verification.json') -Encoding utf8
Write-Output "PACKAGE PASS: $Version; $count decoded payloads; $dataCount Data hashes; embedded public signature and exact EXE match; branded resources; RandyPick.exe; enabled test-channel update support; live/scoring remain false."
