> SUPERSEDED FOR JASS: Korean locale 1042 succeeds. Read LOCALE-SUCCESS.md. Neutral-locale observations below remain historical evidence.

# StormLib normal read-only investigation

## Definitive outcome
Official StormLib x64 opens both original maps using SFileOpenArchive(path,0,0x100), with only STREAM_FLAG_READ_ONLY. Both process runs exited normally. All five old 2.314 members read fully and SHA-256 matches the existing SFmpq control evidence. For 2.320, four of five requested members read fully. Root war3map.j opens as an 88-byte entry but first SFileReadFile fails, Windows error 1392 (ERROR_FILE_CORRUPT), zero bytes. Standard scripts\war3map.j is absent (error 2). No successful 2.320 JASS extraction, and no claim that all map content is readable.

The old blanket conclusion '2.320 cannot be read by a normal reader' is disproved. The narrower 'root JASS cannot be read through the tested normal API' remains supported. This does not establish the root cause of older readers' failures or prove the whole archive corrupt.

## 2.320 member results
|Member|Length|Block index|Compressed bytes|Result / SHA256|
|---|---:|---:|---:|---|
|war3map.j|88|4306|88|Open succeeds, read fails 1392|
|war3map.w3u|917401|1754|47640|18B5DBA815B7BAED4E56683136883E9B239FC4E8AC63491BC57696FB05586B2A|
|war3map.w3a|699383|1748|41113|D3FDE16020FAF5F3E0BECDBF571927D3ACD77BE9D5610F3E07A2B2584AC60AA5|
|war3map.wts|1816858|1|168848|4B9C531CDE67C979EAE0BA289D133862F57714CD7DC0CF3BBAC3376B52EE6946|
|war3mapMisc.txt|1183|7|532|8428C5E040AC9A75CE37209145B25B6640C560F5D96E5F068E589946317BF140|

All five flags are 0x80010200: EXISTS, ENCRYPTED, COMPRESS. These are ordinary named-member API calls; no explicit keys supplied and no alternative keys attempted. The miscellaneous file exactly matches 2.314; the other three successfully read files differ. The successfully extracted w3u/w3a have a little-endian version 3 first DWORD. WTS is NOT valid strict UTF-8, so parent should not assume UTF-8 for semantic parsing.

## Documented implementation handling
Source pinned to official v9.40 commit 6bb1882bd00ddbc3729cac5dac0fda81a61e5514.
- Important naming: raw header wFormatVersion=1 is StormLib MPQ_FORMAT_VERSION_2, not StormLib's version-1 format (whose raw field is 0). Extended high block tables belong to raw=1 format.
- SFileOpenArchive.cpp lines 92-94 recognizes HM3W plus zero second DWORD as Warcraft III map; .w3x extension is also recognized.
- SBaseFileTable.cpp ConvertMpqHeaderToFormat4, line 485, automatically chooses format-1 compatibility for Warcraft III maps as normal built-in behavior. The caller did not use MPQ_OPEN_FORCE_MPQ_V1. Internally it normalizes the header in memory, flags malformed, and discards extended-header fields. Original files are never written.
- Even without Warcraft III detection, raw format=1 with nonstandard headerSize falls back to format-1 compatibility at lines 571-580, rather than treating the enormous header size as a data prefix.
- Format-1 branch masks sector exponent to low 8 bits (line 538 onward); exponent 21 remains 21.
- SFileOpenArchive.cpp lines 420-422 rejects zero exponent; lines 491-493 calculate 0x200 << exponent and reject only a zero result. Exponent 21 yields 1,073,741,824 bytes (1 GiB), nonzero. No check that a sector is smaller than archive or member is present here. Such an anomalous exponent is not, by itself, proof that member blocks are invalid.
- SBaseCommon.cpp lines 1155-1157 allocate a sector-sized internal buffer for non-single-unit members. For SINGLE_UNIT the member size is used instead. IMPORTANT: our 64 MiB extraction size cap and 64 KiB user buffers do not constrain native internal sector allocation, potentially 1 GiB here. Runs were isolated and completed within 60 seconds each.
- Runtime confirms sectorSize=1073741824 for 2.320; 4096 for 2.314. archiveFlags=1029 (0x405) for both, meaning READ_ONLY | MALFORMED | WAR3_MAP. Runtime fileTableSize=65536. All queried named block indices lie below parent-measured 64749 on-disk block count. No basis to blame all valid blocks just on sectorShift.

## Provenance
Official asset: https://github.com/ladislav-zezula/StormLib/releases/download/v9.40/stormlib_dll.zip
Release: https://github.com/ladislav-zezula/StormLib/releases/tag/v9.40 (2026-07-04)
ZIP SHA256 B2C9635E7B63EDEE1BD7C82E7DC180D739F3ACCB2B8994804C7774E464CE89AE
x64 DLL SHA256 93321F6F030BE5D7D79EB4CBF433D6EF7E83CE20D47D8963717F207D54AFBE16
PE machine=8664, process Is64BitProcess=true.
Caveat: DLL Windows resource reports FileVersion/ProductVersion 9.26.0.3 although official release asset is v9.40, History.txt says 9.40, and new SFileOpenFileArchive export exists. release.sha256 omits stormlib_dll.zip, so do NOT claim manifest checksum verification. Provenance is official HTTPS GitHub release asset with recorded local hashes.
Official source URLs: https://github.com/ladislav-zezula/StormLib/blob/v9.40/src/SFileOpenArchive.cpp ; https://github.com/ladislav-zezula/StormLib/blob/v9.40/src/SBaseFileTable.cpp ; https://github.com/ladislav-zezula/StormLib/blob/v9.40/src/SBaseCommon.cpp ; https://github.com/ladislav-zezula/StormLib/blob/v9.40/src/StormLib.h

## Files for parent
Everything is under this modern-reader directory.
- result-2.320.json, result-2.314.json: full measured read results and before/after archive SHA256.
- metadata-2.320.json: normal API member index/size/flags.
- script-result-2.320.json: standard scripts path absent.
- members-2.320/: four successful outputs. Failed empty war3map.j removed to avoid confusion.
- members-2.314/: all five control outputs.
- probe.ps1: runnable temporary x64 PowerShell/Add-Type PInvoke helper. SFileGetFileSize cap 64 MiB per member; sequential 64 KiB reads with exact-length verification. SFileOpenFileEx uses scope 0. Archive input is read-only; output only tmp. Existing outputs use CreateNew, so use a fresh output directory for reruns.
- provenance.json and official sources/DLL.

Archive hashes before and after both normal-read runs are identical:
2.320 68445631FDACA12A343E9465E5BC5DC9B4C8C6CC927D701F52E2CB822821E15F
2.314 F9DDD3AF7C0FBFD39B7A6DF2CA0F5F295675BBA5E8B91A5322DCEC93D7CA8A83
No input/header repair, archive writes, alternative keys, game access, MemoryDiagnostics access, or production source modifications. No browser tabs opened.

