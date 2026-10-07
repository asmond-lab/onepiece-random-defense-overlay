param([string]$SourceRoot=([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))))
$ErrorActionPreference='Stop'
$raw=Get-Content -LiteralPath (Join-Path $SourceRoot 'Data/memory-profiles.json') -Raw
$guard=Join-Path $PSScriptRoot 'Assert-RandypickReferenceProfiles.ps1'
$profiles=$raw|ConvertFrom-Json -NoEnumerate
& $guard -Profiles $profiles
$cases=[ordered]@{
 'missing-modern'={param($p) ,@($p[0])}
 'duplicate-modern'={param($p) ,@($p[0],$p[1],$p[1])}
 'wrong-build'={param($p) $p[1].fileVersion='3.0.0.99999';,$p}
 'wrong-hash'={param($p) $p[1].sha256=('0'*64);,$p}
 'wrong-layout'={param($p) $p[1].layout='Legacy';,$p}
 'wrong-owner-width'={param($p) $p[1].ownerFieldBytes=8;,$p}
 'enabled-modern'={param($p) $p[1].enabled=$true;,$p}
 'verified-modern'={param($p) $p[1].verified=$true;,$p}
 'missing-flag'={param($p) $p[1].PSObject.Properties.Remove('enabled');,$p}
 'string-flag'={param($p) $p[1].enabled='false';,$p}
 'extra-modern-field'={param($p) $p[1]|Add-Member NoteProperty unexpected $true;,$p}
 'missing-legacy'={param($p) ,@($p[1])}
 'duplicate-legacy'={param($p) ,@($p[0],$p[0],$p[1])}
 'legacy-revision-drift'={param($p) $p[0].profileRevision=6;,$p}
 'legacy-pointer-drift'={param($p) $p[0].countOffset=2970;,$p}
 'legacy-disabled'={param($p) $p[0].enabled=$false;,$p}
 'legacy-array-drift'={param($p) $p[0].pointerOffsets=@(1);,$p}
}
foreach($name in $cases.Keys){
 $inputProfiles=$raw|ConvertFrom-Json -NoEnumerate
 $changed=& $cases[$name] $inputProfiles
 $rejected=$false
 try{& $guard -Profiles $changed|Out-Null}catch{$rejected=$true}
 if(-not $rejected){throw "Guard incorrectly accepted: $name"}
 Write-Output "REJECT PASS: $name"
}
Write-Output "PROFILE REGRESSION PASS: valid bundle accepted; $($cases.Count) malformed or changed profiles rejected."
