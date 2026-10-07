# 2.320 bounded source metadata regeneration

Run `node optionaltools/Map2320MetadataGenerator/generate.cjs` from any directory. Optional first argument selects the output file. Requires the eight extracted files in docs/analysis-2320/modern-reader/members-ko-2.320, the confirmed-source-manifest, mpq-headers, and the independently parsed 2.320.w3u.json. No legacy metadata is read. No external archive is opened and no runtime compatibility or archive approval is conferred.

All eight extracted lengths/SHA256 values are checked against the confirmed manifest before parsing. JASS uses the confirmed Korean locale 1042 member. The bounded W3U parser consumes both complete tables and all modifications, then cross-checks selected records and every selected modification against the archived independent parse. All modifications of the seven target records are retained. WTS offsets are byte-based and SHA256 includes each complete record from STRING through the closing brace line ending. JASS objective creation pins and table are found from rawcode expressions. Short reward factories are selected structurally, never by an inherited function name.

API: Map2320SourceMetadata.LoadBundled(), Load(ReadOnlySpan<byte>), ValidateSourceMembers(MapSourceMetadata, IReadOnlyDictionary<string, byte[]>), ComputeSemanticHash(MapSourceMetadata). Returns existing immutable MapSourceMetadata records. Legacy serialized types were not modified. LoadBundled checks exact data bytes; Load allows only formatting/property ordering differences. Canonical semantic SHA256 fixes every required property/value, array order, offset, count, range and source pin; duplicates, unknown fields, null, booleans and non-integer numbers fail closed.

Data SHA256: e6f53445f8bc0dd0f174bd5fab0eb095295c19c8f40b32e657990a3c0a8311e6

Canonical semantic SHA256: 74219edfdc91b6816f9d09696ab40077a210b7d97cd85ecf6cc34e1d982c63bc

## Verification performed

Independent regeneration was byte-identical. 21 isolated tests passed with ORAND_2320_SOURCE_MEMBERS pointing to extracted sources, including all eight complete source byte hashes and WTS record slices. Another 21-test run passed without that variable, depending only on copied Data JSON, not docs. Optional source test is a no-op when the variable is absent.

Evidence: .metadata2320-artifacts/regeneration-evidence.json, isolated-test.log, packaged-test.log and results/*.trx. Isolated test assembly product version 0.6.70, SHA256 6c2c682c844f514c15287a14eb6a8db2a4019c4bb1a032077aa0867e5226d551.

The full project build was blocked by an unrelated concurrent compile error in RecipeCompletionCalculator.cs:204 (IDictionary<string,long> cannot convert to IReadOnlyDictionary<string,long>). No out-of-scope fix was made. Therefore compilation/test evidence uses MetadataTests.csproj linking the new source/tests against the existing public DTOs in .bullet2320-artifacts/bin/OrandOverlay/debug/OrandOverlay.dll, not a claim that the full application builds.

Reproduce isolated tests in PowerShell (set ProgramFiles(x86)/ProgramFiles in stripped process environments if NuGet raises ConfigurationDefaults path1):

```powershell
$r='C:\Users\123\Desktop\dev\orand-overnight-overlay-20260907-111725'
${env:ProgramFiles(x86)}='C:\Program Files (x86)'
$env:ProgramFiles='C:\Program Files'
$env:ORAND_2320_SOURCE_MEMBERS="$r\docs\analysis-2320\modern-reader\members-ko-2.320"
dotnet test "$r\optionaltools\Map2320MetadataGenerator\MetadataTests.csproj" -p:UseArtifactsOutput=true -p:ArtifactsPath="$r\.metadata2320-artifacts" -p:LegacyDtoAssembly="$r\.bullet2320-artifacts\bin\OrandOverlay\debug\OrandOverlay.dll"
```

Parent integrator owns final application build and bundle manifest. Data already matches the project's existing Data content glob; root validator and test file match existing compile globs.
