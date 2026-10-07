# 2.321 diagnostic round wiring: offline verification

- Versioned bundled growth schema is hash/length/source checked: 2.320 QR/pb/Eb/qg; 2.321 Vu/Ag/lg/yp.
- 2.321 normalized schema: 52397 bytes, SHA256 8B151B4FA743E2944C805BF1ADE7F127FBB867731A98DC02E04AEB961E1E196C; 3926 declarations.
- App diagnostic service passes the selected versioned source into GrowthReader and ObservedRoundReader; basic-list source selection is also version-aware. Data glob already packages the JSON, no csproj addition needed.
- Production AllowsReader remains 2.320-only (besides legacy 2.314). Diagnostic reference observation is separate. No profile approval, native memory writes, debugger, live process reads, deployment or game actions performed in this change.
- Focused ObservedRoundReader tests: 39 passed. Growth/Round/Map2320/DiagnosticInventory regression filter: 677 passed, 0 failed, 0 skipped. Build succeeds with existing warnings.
- Integration test uses actual bundled declarations and SYNTHETIC memory payload 12/13 and timer title 12. It proves code wiring, not real runtime layout or actual game round. Wrong version, tampered schema and title mismatch reject.
- Stored live-2321-scalars capture: ProtocolRejected at complete-growth-discovery; Evidence=null, ReferenceRound=null. Failure cause is not established. List capture ZIPs contain only snapshot.json/report.md, not replayable JASS scalar/title memory bytes.
- [blocked] Live 2.321 round verification requires a running 2.321 game, a fresh read-only diagnostic capture and independent on-screen round comparison. Do not infer actual round from synthetic 12 or Vu array length.
