# Successful Korean-locale JASS extraction

This result supersedes REPORT.md's neutral-locale JASS blocker.

Normal public API: SFileSetLocale(1042), SFileOpenArchive(path,0,0x100), SFileOpenFileEx(archive,"war3map.j",0,...), SFileGetFileSize, sequential bounded SFileReadFile. No force flags, keys, or input modifications. Same official x64 DLL/provenance as prior report.

2.320 output: members-ko-2.320\war3map.j
Length: 3014709
SHA256: 6FDFC64BF8AD9463F5B5C8A351FFA7E1875D6CF9B51210129539375FA77A6C7C
Strict UTF-8 succeeds with exact byte roundtrip; zero NULs; 86599 lines (LF count+1), 2650 function declarations and 2650 endfunction declarations. CP949 strict decoding fails, consistent with UTF-8 Korean text. These are readability/structure checks, not JASS semantic validation.

2.314 control with the same locale selection: 3069240 bytes, SHA256 0BCCC47907A9505F38EFAF6BBF20228A728EABDFAEC3209CCA7DF2269BFC2028, matching existing control.

Before/after original archive hashes remain identical for both runs. Both isolated x64 processes exit 0. Evidence: result-ko-2.320.json, result-ko-2.314.json, probe-ko.ps1.

## WTS encoding
Both 2.320 and 2.314 start EF BB BF (UTF-8 BOM), followed by ASCII STRING 0 and UTF-8 Korean comments. Whole-file strict UTF-8 fails because exactly five comment lines contain incomplete UTF-8 sequences. The same five comment line numbers occur in both versions: 1521,1527,1767,2335,2341. First 2.320 failure at zero-based byte 33870 is EB immediately followed by ')' (29), a truncated UTF-8 sequence. Another malformed comment ends in EC before ')'.

For 2.320, 25681 non-ASCII lines: 24860 strict UTF-8-only; 816 valid under both UTF-8 and CP949; 0 CP949-only; 5 invalid under both. All five UTF-8-invalid lines are // comments outside string blocks. No UTF-8-invalid string-value lines were found by brace-delimited line inspection.
For 2.314, corresponding counts 24974 / 24165 / 804 / 0 / 5; same invalid comment line numbers.

Conclusion: evidence supports UTF-8 WTS with five malformed/truncated comment lines, not CP949 and not demonstrated mixed UTF-8/CP949 content. Do not decode whole file with replacement fallbacks or reinterpret as CP949. For parsing, preserve original bytes, recognize ASCII WTS syntax/comment boundaries, ignore comments as comments, and strict UTF-8 decode actual value lines. No source or extracted bytes were rewritten.

Evidence: encoding-summary.json and wts-line-encoding.json. All output confined to temporary workspace. No browser tabs opened.
