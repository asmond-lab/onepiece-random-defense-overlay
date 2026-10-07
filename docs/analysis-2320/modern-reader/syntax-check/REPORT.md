# Full-file JASS syntax validation: PASS

Both strict-UTF-8 source files parsed completely with the official War3Net.CodeAnalysis.Jass 6.0.2 assembly, using JassSyntaxFactory.ParseCompilationUnit(string). There were no syntax errors, so error line/column/offset lists are empty, not unknown failures.

Pinned packages: War3Net.CodeAnalysis.Jass 6.0.2, War3Net.CodeAnalysis 6.0.2, War3Net.Common 6.0.2, Pidgin 3.1.0. NuGet's package version index did not publish Jass 6.0.3. A temporary .NET project was attempted, but the local NuGet ConfigurationDefaults initializer failed with a null path. The official assemblies were therefore loaded directly in PowerShell's .NET host. Package hashes, loaded assembly identities and host version are recorded in parser-manifest.json.

| Measure | New 2.320 | Old indexed 2314 |
|---|---:|---:|
| Input bytes | 3,014,709 | 3,069,240 |
| UTF-16 characters | 2,962,829 | 3,020,186 |
| Top-level declarations | 2,651 | 2,695 |
| Function declarations | 2,650 | 2,694 |
| Globals blocks | 1 | 1 |
| Global declarations | 3,936 | 3,823 |
| Native declarations in these files | 0 | 0 |
| Type declarations in these files | 0 | 0 |
| Syntax errors | 0 | 0 |

Full-file assurance: AST.ToFullString() exactly equaled each complete decoded input. A negative control consisting of a valid function followed by @@@ was rejected at line 3, column 1 (not run as code). Input SHA-256 checks before and after matched.

New SHA-256: 6FDFC64BF8AD9463F5B5C8A351FFA7E1875D6CF9B51210129539375FA77A6C7C
Old SHA-256: 0BCCC47907A9505F38EFAF6BBF20228A728EABDFAEC3209CCA7DF2269BFC2028

See additional-metadata.json for line-anchored call-statement counts, AST invocation-expression counts, and global scalar/array counts. ast-metadata.json preserves the parser's GetDescendantNodes() type counts; that API's enumeration is not claimed to include every intermediate AST node. Calls are structural occurrences, not execution frequencies, resolution checks, or native-call classification.

All writes were confined to this syntax-check directory. Source files were read only. No map/source/game execution, archive changes, production edits, automatic source fixes, or MemoryDiagnostics were performed. The validation itself was local; parser dependency acquisition used NuGet HTTPS downloads. No browser tabs were opened.

Scope: syntax only. These passes do not prove name resolution, types, native availability, gameplay correctness, semantic equivalence, or compatibility with a particular Warcraft III runtime. Obfuscated names and function-count differences were not treated as semantic evidence.
