param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Profiles)
$ErrorActionPreference='Stop'

$modern=@($Profiles|Where-Object {$_.fileVersion -like '3.0*' -or $_.profileId -ceq 'war3-3.0.0.24268-reference'})
if($modern.Count -ne 1){throw 'Expected exactly one pinned 3.0 reference profile'}
$expected=[ordered]@{
 profileSchemaVersion=2;profileId='war3-3.0.0.24268-reference';profileRevision=1
 fileVersion='3.0.0.24268';moduleName='Warcraft III.exe';enabled=$false;verified=$false
 sha256='BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12'
 layout='Warcraft30024268Diagnostic';ownerFieldBytes=4;locatorKind='StructuralScan'
 countOffset=3080;entriesPointerOffset=3088;ownerOffset=448;rawcodeOffset=376;minimumUnitObjects=1
}
if(@($modern[0].PSObject.Properties).Count -ne $expected.Count){throw 'Reference profile fields differ from the pinned contract'}
foreach($name in $expected.Keys){
 $actual=$modern[0].PSObject.Properties[$name]
 if($null -eq $actual -or ($actual.Value|ConvertTo-Json -Compress) -cne ($expected[$name]|ConvertTo-Json -Compress)){
  throw "Pinned reference profile mismatch: $name"
 }
}
$legacy=@($Profiles|Where-Object {$_.fileVersion -ceq '2.0.4.23745' -or $_.profileId -ceq 'war3-2.0.4.23745'})
if($legacy.Count -ne 1){throw 'Expected exactly one retained legacy profile'}
$legacyExpected=[ordered]@{
 profileSchemaVersion=1;profileId='war3-2.0.4.23745';profileRevision=5
 fileVersion='2.0.4.23745';moduleName='Warcraft III.exe';enabled=$true;verified=$true
 sha256='682C12552CA05E43C5FED2340EA132D3B06FE068E676DB7D1F5623D8D4633229'
 locatorKind='StructuralScan';moduleOffset=0;signature='';relativeDisplacementOffset=0;instructionLength=0
 pointerOffsets=@();countOffset=2968;entriesPointerOffset=2976;entriesAreInline=$false;entryStride=8
 entryPointerOffset=0;entriesContainPointers=$true;ownerPointerOffsets=@();ownerOffset=448
 rawcodePointerOffsets=@();rawcodeOffset=376;localPlayerSlot=0;maximumUnits=8191
 requireNonEmptyInventory=$true;minimumCatalogMatchRatio=0.2;unitClassName='.?AVCUnit@@';minimumUnitObjects=8
 localPlayerRootOffsetA=45455716;localPlayerRootOffsetB=45972424;localPlayerRootXor='363ABBFD3DEFAEC9';localPlayerIdOffset=9828
}
if(@($legacy[0].PSObject.Properties).Count -ne $legacyExpected.Count){throw 'Legacy profile fields changed'}
foreach($name in $legacyExpected.Keys){
 $actual=$legacy[0].PSObject.Properties[$name]
 if($null -eq $actual -or (ConvertTo-Json -InputObject $actual.Value -Compress) -cne (ConvertTo-Json -InputObject $legacyExpected[$name] -Compress)){
  throw "Retained legacy profile mismatch: $name"
 }
}
Write-Output 'PROFILE CONTRACT PASS: exact disabled/unverified 3.0 reference and unchanged legacy rev5.'
