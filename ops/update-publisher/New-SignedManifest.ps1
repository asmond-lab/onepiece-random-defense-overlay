param(
    [Parameter(Mandatory)][ValidateSet('application','memory-profiles')][string]$Kind,
    [Parameter(Mandatory)][ValidateSet('stable','test')][string]$Channel,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][Alias('ArtifactPath')][string]$AssetPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$KeyId='orand-p256-20260913',
    [string]$KeyFile=(Join-Path $env:LOCALAPPDATA 'OrandOverlayPublisher\keys\cloudflare-update-p256.dpapi')
)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'The desktop signing key requires Windows CurrentUser DPAPI.'}
if($Version -notmatch '\A(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-test\.[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*)?\z'){throw 'Expected a three-component release version, optionally with a test suffix.'}
if($Version.Contains('-')){foreach($part in $Version.Split('-')[1].Split('.')){if($part -match '\A0\d+\z'){throw 'Numeric test identifiers cannot have leading zeroes.'}}}
$parsedVersion=$null
if($Version.Length -gt 64 -or -not [Version]::TryParse($Version.Split('-')[0],[ref]$parsedVersion)){throw 'Release version exceeds client limits.'}
if(($Channel -eq 'stable' -or $Kind -eq 'memory-profiles') -and $Version.Contains('-')){throw 'Stable/profile versions must be numeric.'}
if($Kind -eq 'memory-profiles' -and $Channel -ne 'stable'){throw 'Only verified stable memory profiles may be published.'}
if($Channel -eq 'test' -and -not $Version.Contains('-test.')){throw 'Test-channel artifacts require an explicit test version.'}
$asset=[IO.Path]::GetFullPath($AssetPath)
# Application names are a case-sensitive, exact allowlist, never an extension rule.
$assetName=[IO.Path]::GetFileName($asset)
if($Kind -eq 'application' -and $assetName -cnotin @('OrandOverlay.exe','RandyPick.exe')){throw 'Application artifact filename is not allowlisted.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$trust=Get-Content (Join-Path $root 'Data\update-trust.json') -Raw|ConvertFrom-Json
$trusted=@($trust.keys|Where-Object {$_.keyId -ceq $KeyId})
if($trusted.Count -ne 1 -or $trusted[0].algorithm -cne 'ECDSA-P256-SHA256-P1363'){throw 'Unknown public trust key.'}
$length=(Get-Item -LiteralPath $asset).Length
$maximum=if($Kind -eq 'application'){512MB}else{2MB}
if($length -le 0 -or $length -gt $maximum){throw 'Asset exceeds its publication bounds.'}
if($Kind -eq 'application'){
    $versionInfo=[Diagnostics.FileVersionInfo]::GetVersionInfo($asset)
    $productVersion=$versionInfo.ProductVersion
    $expectedFileVersion=[Version]::new($parsedVersion.Major,$parsedVersion.Minor,$parsedVersion.Build,0)
    $actualFileVersion=[Version]::new($versionInfo.FileMajorPart,$versionInfo.FileMinorPart,$versionInfo.FileBuildPart,$versionInfo.FilePrivatePart)
    if($actualFileVersion -ne $expectedFileVersion){throw 'Executable PE file version differs from release core version.'}
    if($productVersion -cne $Version){throw 'Executable informational version differs from release version.'}
    if($Channel -eq 'stable' -and $productVersion -match 'test'){throw 'A test executable cannot become an official release by relabeling.'}
    $relative="/downloads/$Version/$assetName"
}else{
    $profiles=Get-Content -LiteralPath $asset -Raw|ConvertFrom-Json -NoEnumerate
    if($profiles -isnot [Array] -or $profiles.Count -eq 0){throw 'Expected a nonempty profile array.'}
    foreach($profile in $profiles){
        if($profile.enabled -ne $true -or $profile.verified -ne $true -or $profile.sha256 -notmatch '\A[0-9a-fA-F]{64}\z'){
            throw 'Unverified or invalid memory profiles cannot be signed for publication.'
        }
    }
    $relative="/profiles/$Version/memory-profiles.json"
}
$digest=(Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
$payload=[ordered]@{
    schemaVersion=1;kind=$Kind;channel=$Channel;version=$Version;platform='win-x64';minimumUpdaterVersion=1
    publishedAtUtc=[DateTime]::UtcNow.ToString('O')
    asset=[ordered]@{path=$relative;size=$length;sha256=$digest}
}
$payloadBytes=[Text.UTF8Encoding]::new($false).GetBytes(($payload|ConvertTo-Json -Depth 5 -Compress))
Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
if(-not ('OrandPublisherCrypto' -as [type])){
    Add-Type -TypeDefinition @'
using System.Security.Cryptography;
public static class OrandPublisherCrypto {
    public static void Import(ECDsa key, byte[] data) { key.ImportPkcs8PrivateKey(data, out _); }
    public static byte[] Sign(ECDsa key, byte[] data) { return key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation); }
    public static bool Verify(ECDsa key, byte[] data, byte[] signature) { return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation); }
}
'@
}
$key=[Security.Cryptography.ECDsa]::Create()
try{
    $private=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($KeyFile),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    try{[OrandPublisherCrypto]::Import($key,$private)}finally{[Array]::Clear($private,0,$private.Length)}
    if($key.KeySize -ne 256 -or [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo()) -cne $trusted[0].publicKeySpki){throw 'Private key does not match embedded public trust.'}
    $signature=[OrandPublisherCrypto]::Sign($key,$payloadBytes)
    if(-not [OrandPublisherCrypto]::Verify($key,$payloadBytes,$signature)){throw 'Signature self-verification failed.'}
    $envelope=[ordered]@{schemaVersion=1;keyId=$KeyId;payload=[Convert]::ToBase64String($payloadBytes);signature=[Convert]::ToBase64String($signature)}
    [IO.Directory]::CreateDirectory($output)|Out-Null
    $destination=Join-Path $output 'manifest.json'
    if([IO.File]::Exists($destination)){throw 'Manifest already exists; release inputs are immutable.'}
    [IO.File]::WriteAllText($destination,($envelope|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
    $pointer=if($Kind -eq 'application'){"channels/$Channel/win-x64.json"}else{'channels/profiles/win-x64.json'}
    Write-Output "SIGNED: $Kind/$Channel/$Version; SHA256=$digest; bytes=$length"
    Write-Output "ASSET_R2_KEY=$($relative.TrimStart('/'))"
    Write-Output "MANIFEST_R2_KEY=$pointer"
    Write-Output 'No upload performed. Publish the verified immutable asset first, then its channel manifest.'
}finally{$key.Dispose()}
