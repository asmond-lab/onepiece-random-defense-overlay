using System.Globalization;
using System.IO;

namespace WarcraftProbe;

/// <summary>Pure comparison of bounded metadata. Never promotes observations to gameplay truth.</summary>
public static class SnapshotDiff
{
    private const int MaxObjects = 32768;
    private static readonly StringComparer Ordinal = StringComparer.Ordinal;
    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string X(uint value) => $"0x{value:X8}";
    private static string V(object? value) => value is null ? "Unknown" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "Unknown";

    public static void Validate(Snapshot snapshot)
    {
        if (snapshot is null) Bad("snapshot is null");
        if (snapshot!.SchemaVersion != 1) Bad("Only schema 1 is supported");
        Text(snapshot.ToolVersion, "ToolVersion", 128);
        Text(snapshot.CaptureId, "CaptureId", 128);
        Text(snapshot.Mode, "Mode", 64);
        Text(snapshot.Label, "Label", 512, true);
        if (snapshot.FinishedAt < snapshot.StartedAt) Bad("Capture time is reversed");
        var image = snapshot.Image;
        if (image is null) Bad("Image is null");
        Text(image!.FileName, "Image.FileName", 1024);
        if (image.FileSize <= 0 || image.FileSize > 16L * 1024 * 1024 * 1024) Bad("Invalid file size");
        Hash(image.Sha256, "Image.Sha256");
        if (image.FileVersion is not null) Text(image.FileVersion, "FileVersion", 256, true);
        if (image.ImageSize == 0 || image.HeaderSize > image.ImageSize || image.EntryPointRva >= image.ImageSize) Bad("Invalid image bounds");
        Items(image.Sections, 96, "Sections");
        Items(image.Anchors, 1000, "Anchors");
        foreach (var section in image.Sections)
        {
            if (section is null) Bad("Null section");
            Text(section!.Name, "Section.Name", 128, true);
            Range(section.Rva, section.VirtualSize, image.ImageSize, "Section");
            if ((ulong)section.RawOffset + section.RawSize > (ulong)image.FileSize) Bad("Section raw data exceeds file");
            Hash(section.Sha256, "Section.Sha256");
        }
        foreach (var anchor in image.Anchors)
        {
            if (anchor is null) Bad("Null anchor");
            Text(anchor!.Kind, "Anchor.Kind", 128);
            Text(anchor.Name, "Anchor.Name", 512);
            Text(anchor.Status, "Anchor.Status", 128);
            Items(anchor.Rvas, 256, "Anchor.Rvas");
            var seen = new HashSet<uint>();
            foreach (var rva in anchor.Rvas)
            {
                Range(rva, 1, image.ImageSize, "Anchor");
                if (!seen.Add(rva)) Bad("Duplicate RVA in anchor");
            }
        }
        var live = snapshot.Live;
        if (live is null) return;
        if (live.ProcessId <= 0 || live.ModuleSize == 0) Bad("Invalid process/module");
        if (live.RequestedBytes < 0 || live.RequestedBytes > 16L * 1024 * 1024 * 1024 || live.QueryCalls < 0 || live.QueryCalls > 1000000) Bad("Invalid read accounting");
        Duration(live.ElapsedMilliseconds, "Live.ElapsedMilliseconds");
        Text(live.BindingScope, "BindingScope", 1024, true);
        Text(live.Status, "Live.Status", 128);
        Items(live.Regions, 50000, "Regions");
        Items(live.CodeChecks, 1000, "CodeChecks");
        foreach (var region in live.Regions)
        {
            if (region is null) Bad("Null region");
            if (region!.Size == 0) Bad("Empty region");
            Range(region.Rva, region.Size, live.ModuleSize, "Region");
        }
        foreach (var check in live.CodeChecks)
        {
            if (check is null) Bad("Null code check");
            Text(check!.Name, "CodeCheck.Name", 512);
            Text(check.Status, "CodeCheck.Status", 128);
            Range(check.Rva, check.Size, live.ModuleSize, "CodeCheck");
            if (check.Sha256 is not null) Hash(check.Sha256, "CodeCheck.Sha256");
        }
        var structure = live.Structures;
        if (structure is null) Bad("Structures is null");
        Text(structure!.ProfileId, "ProfileId", 256, true);
        Text(structure.Status, "Structures.Status", 128);
        Duration(structure.ElapsedMilliseconds, "Structures.ElapsedMilliseconds");
        if (structure.CurrentViewSlot is < 0 or > MaxObjects) Bad("Invalid current-view slot");
        Count(structure.ObservedObjectCount, "ObservedObjectCount");
        Count(structure.CurrentViewObjectCount, "CurrentViewObjectCount");
        Count(structure.OtherViewObjectCount, "OtherViewObjectCount");
        foreach (var token in new[] { structure.WorldToken, structure.VmToken, structure.UiToken })
            if (token is not null) Text(token, "Structure token", 512);
        if (structure.Reason is not null) Text(structure.Reason, "Structure reason", 4096, true);
        Items(structure.Rawcodes, MaxObjects, "Rawcodes");
        var codes = new HashSet<string>(Ordinal);
        long total = 0;
        foreach (var raw in structure.Rawcodes)
        {
            if (raw is null) Bad("Null rawcode count");
            Text(raw!.Rawcode, "Rawcode", 128);
            Count(raw.Count, "Rawcode.Count");
            if (!codes.Add(raw.Rawcode)) Bad("Duplicate rawcode count");
            total += raw.Count;
        }
        if (total > MaxObjects) Bad("Rawcode total exceeds bound");
        if (structure.Status == "Observed")
        {
            if (structure.ObservedObjectCount is null || structure.CurrentViewObjectCount is null || structure.OtherViewObjectCount is null)
                Bad("Observed structure requires full row counts");
            if (structure.CurrentViewObjectCount + structure.OtherViewObjectCount != structure.ObservedObjectCount)
                Bad("Incoherent observed row counts");
            if (total != structure.CurrentViewObjectCount) Bad("Observed rawcodes must cover every CURRENTVIEW allocated row");
        }
    }

    private static void Bad(string reason) => throw new InvalidDataException(reason);
    private static void Text(string? value, string name, int max, bool empty = false)
    {
        if (value is null || value.Length > max || (!empty && string.IsNullOrWhiteSpace(value))) Bad("Invalid " + name);
        foreach (var c in value!)
            if (char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                Bad("Invalid control character in " + name);
    }
    private static void Hash(string? hash, string name)
    {
        // SHA-256 is 32 bytes, represented by exactly 64 hexadecimal characters.
        if (hash is null || hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) Bad("Invalid SHA-256: " + name);
    }
    private static void Items<T>(T[]? values, int max, string name)
    {
        if (values is null || values.Length > max) Bad("Invalid " + name + " array");
    }
    private static void Count(int? value, string name)
    {
        if (value is < 0 or > MaxObjects) Bad("Invalid " + name);
    }
    private static void Duration(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 86400000) Bad("Invalid " + name);
    }
    private static void Range(uint rva, ulong size, uint bound, string name)
    {
        if (rva >= bound || size > (ulong)bound - rva) Bad(name + " RVA is outside bounds");
    }

    public static Comparison Compare(Snapshot before, Snapshot after)
    {
        Validate(before);
        Validate(after);
        var differences = new List<Difference>();
        var limits = new List<string>
        {
            "읽기 전용 메타데이터 및 한정 구조 관측 비교입니다. 로컬 소유·생존·전체 게임플레이 목록·지금 제작 가능 여부는 항상 Unknown입니다.",
            "NativeNameString/NodeNames 문자열은 이름 데이터이며 함수·vtable 또는 실제 실행 코드의 증거가 아닙니다. 필드 의미를 추론하지 않습니다.",
            "복사본 SHA는 런타임 전체 이미지 해시가 아닙니다. 자동 프로필 승인 및 GameplayReady는 항상 false입니다.",
            "관측은 원자적이지 않습니다. 변경된 RVA·섹션·코드 검사와 구조 상태는 수동 리더 검토가 필요하며 완료 예상 시간은 산정하지 않습니다."
        };
        void Add(string area, string name, object? oldValue, object? newValue, string meaning)
        {
            string left = V(oldValue), right = V(newValue);
            if (!Ordinal.Equals(left, right)) differences.Add(new(area, name, left, right, meaning));
        }
        var b = before.Image;
        var a = after.Image;
        bool sameImage = StringComparer.OrdinalIgnoreCase.Equals(b.Sha256, a.Sha256);
        bool sameProcess = before.Live is not null && after.Live is not null && before.Live.ProcessId == after.Live.ProcessId && before.Live.ProcessStartedAt == after.Live.ProcessStartedAt;
        Add("Image", "Copy SHA-256", b.Sha256.ToUpperInvariant(), a.Sha256.ToUpperInvariant(), "복사본 바이트 동일성만 판정");
        Add("Image", "File version", b.FileVersion, a.FileVersion, "버전 문자열은 빌드 호환성 승인이 아님");
        Add("Image", "Entry point RVA", X(b.EntryPointRva), X(a.EntryPointRva), "진입점 이동; 수동 검토");
        Add("Image", "File size", b.FileSize, a.FileSize, "복사본 크기");
        Add("Image", "Image size", b.ImageSize, a.ImageSize, "이미지 범위");
        Add("Image", "Header size", b.HeaderSize, a.HeaderSize, "헤더 범위");
        Add("Image", "Machine", b.Machine, a.Machine, "대상 아키텍처");
        Add("Image", "Preferred base", $"0x{b.PreferredBase:X}", $"0x{a.PreferredBase:X}", "정적 선호 주소; RVA 비교에는 ASLR 영향 없음");
        Add("Image", "Timestamp", b.TimeDateStamp, a.TimeDateStamp, "타임스탬프는 동일성 증거가 아님");
        var bs = b.Sections.GroupBy(s => s.Name, Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), Ordinal);
        var ass = a.Sections.GroupBy(s => s.Name, Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), Ordinal);
        foreach (var key in bs.Keys.Union(ass.Keys, Ordinal).OrderBy(k => k, Ordinal))
        {
            var left = bs.GetValueOrDefault(key) ?? Array.Empty<SectionInfo>();
            var right = ass.GetValueOrDefault(key) ?? Array.Empty<SectionInfo>();
            if (left.Length > 1 || right.Length > 1)
            {
                limits.Add("중복 섹션 이름 '" + key + "': 일대일 대응 불명. 첫 항목을 선택하지 않고 전체 메타데이터 집합만 비교했습니다.");
                Add("Section ambiguity", key, Sections(left), Sections(right), "중복 이름: 이동 대응 불명, 수동 검토");
                continue;
            }
            if (left.Length == 0 || right.Length == 0)
            {
                Add("Section", key, Sections(left), Sections(right), "섹션 추가/제거 또는 이름 변경; 대응 추정 안 함");
                continue;
            }
            var l = left[0]; var r = right[0];
            Add("Section RVA", key, X(l.Rva), X(r.Rva), "동일 이름 섹션 이동");
            Add("Section size", key, l.VirtualSize, r.VirtualSize, "가상 크기");
            Add("Section raw offset", key, X(l.RawOffset), X(r.RawOffset), "파일 오프셋");
            Add("Section raw size", key, l.RawSize, r.RawSize, "원시 크기");
            Add("Section flags", key, X(l.Characteristics), X(r.Characteristics), "PE 특성 플래그");
            Add("Section hash", key, l.Sha256.ToUpperInvariant(), r.Sha256.ToUpperInvariant(), "복사본 섹션 해시");
        }
        var ba = b.Anchors.GroupBy(x => (x.Kind, x.Name)).ToDictionary(g => g.Key, g => g.ToArray());
        var aa = a.Anchors.GroupBy(x => (x.Kind, x.Name)).ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var key in ba.Keys.Union(aa.Keys).OrderBy(k => k.Kind, Ordinal).ThenBy(k => k.Name, Ordinal))
        {
            var left = ba.GetValueOrDefault(key) ?? Array.Empty<AnchorInfo>();
            var right = aa.GetValueOrDefault(key) ?? Array.Empty<AnchorInfo>();
            var name = key.Kind + ": " + key.Name;
            bool ambiguous = left.Length > 1 || right.Length > 1;
            if (ambiguous) limits.Add("중복 앵커 KIND+NAME '" + name + "': 레코드 대응 불명. 전체 RVA 다중집합만 비교합니다.");
            string meaning = key.Kind == "NativeNameString" ? "문자열 위치만 비교; 함수/vtable/필드 의미 증거 아님" : "동일 KIND+NAME의 전체 RVA 비교; 의미나 실행 가능성 승인 아님";
            Add("Anchor RVA", name, Rvas(left.SelectMany(x => x.Rvas)), Rvas(right.SelectMany(x => x.Rvas)), ambiguous ? "중복 키 대응 불명; " + meaning : meaning);
            Add("Anchor status", name, string.Join(", ", left.Select(x => x.Status).OrderBy(x => x, Ordinal)), string.Join(", ", right.Select(x => x.Status).OrderBy(x => x, Ordinal)), meaning);
            Add("Anchor records", name, left.Length, right.Length, "정적 앵커 레코드 수; 라이브 함수 수가 아님");
        }
        var bl = before.Live; var al = after.Live;
        Add("Live", "Availability", bl is null ? "NoLive" : bl.Status, al is null ? "NoLive" : al.Status, "누락은 Unknown이며 0이 아님");
        if (bl is not null && al is not null)
        {
            Add("Live", "PID", bl.ProcessId, al.ProcessId, "프로세스 식별");
            Add("Live", "Process start", bl.ProcessStartedAt.ToString("O"), al.ProcessStartedAt.ToString("O"), "프로세스 시작 시각");
            Add("Binding", "Scope", bl.BindingScope, al.BindingScope, "제한 바인딩; 런타임 전체 해시 아님");
            Add("Binding", "Header equality", bl.HeaderBytesEqual, al.HeaderBytesEqual, "헤더 바이트 비교");
            Add("Binding", "Resource equality", bl.ResourceBytesEqual, al.ResourceBytesEqual, "리소스 바이트 비교");
            Add("Live", "Module size", bl.ModuleSize, al.ModuleSize, "모듈 범위");
            var bd = RegionDistribution(before); var ad = RegionDistribution(after);
            foreach (var key in bd.Keys.Union(ad.Keys, Ordinal).OrderBy(k => k, Ordinal))
                Add("Region distribution", key, bl.Regions.Length == 0 ? "Unknown" : DistributionValue(bd.GetValueOrDefault(key)), al.Regions.Length == 0 ? "Unknown" : DistributionValue(ad.GetValueOrDefault(key)), "모듈 구간 수/바이트 분포; 객체 개수 아님");
            if (bl.Regions.Length == 0 || al.Regions.Length == 0) limits.Add("구간 자료가 없는 쪽의 보호/상태/섹션 분포는 Unknown이며 0이 아닙니다.");
            var bc = bl.CodeChecks.GroupBy(x => x.Name, Ordinal).ToDictionary(g => g.Key, g => Checks(g), Ordinal);
            var ac = al.CodeChecks.GroupBy(x => x.Name, Ordinal).ToDictionary(g => g.Key, g => Checks(g), Ordinal);
            foreach (var key in bc.Keys.Union(ac.Keys, Ordinal).OrderBy(k => k, Ordinal))
                Add("Code check", key, bc.GetValueOrDefault(key), ac.GetValueOrDefault(key), "검사 RVA/크기/가독성 상태/부분 해시; 전체 코드 실행 증거 아님");
            Add("Structures", "Status", bl.Structures.Status, al.Structures.Status, "미관측/UnsupportedBuild/만료는 Unknown이며 0이 아님");
            Add("Structures", "Profile", bl.Structures.ProfileId, al.Structures.ProfileId, "프로필 이름이 검증을 대신하지 않음");
            Add("Structures", "Reason", bl.Structures.Reason, al.Structures.Reason, "구조 관측 제한 사유");
            Add("Structures", "Current view slot", bl.Structures.CurrentViewSlot, al.Structures.CurrentViewSlot, "뷰 식별자이며 로컬 플레이어 판정이 아님");
            Add("Structures", "World token", bl.Structures.WorldToken, al.Structures.WorldToken, "월드 연속성");
            Add("Structures", "VM token", bl.Structures.VmToken, al.Structures.VmToken, "VM 연속성");
            Add("Structures", "UI token", bl.Structures.UiToken, al.Structures.UiToken, "UI 연속성");
        }
        var blocked = new List<string>();
        if (!sameImage) blocked.Add("복사본 SHA 불일치");
        if (!sameProcess) blocked.Add("동일 PID+시작 시각을 확인할 수 없음");
        if (bl is null || al is null) blocked.Add("라이브 관측 없음");
        else
        {
            var l = bl.Structures; var r = al.Structures;
            if (l.Status != "Observed" || r.Status != "Observed") blocked.Add("양쪽 구조 상태가 Observed가 아님 (" + l.Status + "/" + r.Status + ")");
            if (!SameToken(l.WorldToken, r.WorldToken)) blocked.Add("WorldToken 누락/변경");
            if (!SameToken(l.VmToken, r.VmToken)) blocked.Add("VmToken 누락/변경");
            if (!SameToken(l.UiToken, r.UiToken)) blocked.Add("UiToken 누락/변경");
            if (l.CurrentViewSlot is null || r.CurrentViewSlot is null || l.CurrentViewSlot != r.CurrentViewSlot) blocked.Add("currentViewSlot 누락/변경");
        }
        if (blocked.Count != 0) limits.Add("개수 차이 차단: " + string.Join("; ", blocked) + ". 미관측 값을 0으로 대체하지 않습니다.");
        else
        {
            var l = bl!.Structures; var r = al!.Structures;
            const string scope = "동일 월드·뷰의 관측된 전체 벡터 행 집합 기준. CURRENTVIEW 할당 행이며 로컬/생존/완전 게임플레이 목록 아님";
            Add("Count", "Observed vector rows", l.ObservedObjectCount, r.ObservedObjectCount, "관측 벡터 전체 행; 게임 전체성 의미 없음");
            Add("Count", "CURRENTVIEW allocated rows", l.CurrentViewObjectCount, r.CurrentViewObjectCount, scope);
            Add("Count", "Other-view allocated rows", l.OtherViewObjectCount, r.OtherViewObjectCount, "관측 벡터의 다른 뷰 행; 로컬/생존 판정 없음");
            var lc = l.Rawcodes.ToDictionary(x => x.Rawcode, x => x.Count, Ordinal);
            var rc = r.Rawcodes.ToDictionary(x => x.Rawcode, x => x.Count, Ordinal);
            foreach (var code in lc.Keys.Union(rc.Keys, Ordinal).OrderBy(x => x, Ordinal))
            {
                int oldCount = lc.GetValueOrDefault(code), newCount = rc.GetValueOrDefault(code);
                Add("Rawcode count", code, oldCount, newCount, scope + "; Δ=" + (newCount - oldCount).ToString("+0;-0;0", CultureInfo.InvariantCulture));
            }
            limits.Add("개수 비교 허용: 동일 SHA/PID/시작 시각 및 non-null World/VM/UI/view, 양쪽 Observed. 해당 전체 관측 행 집합 안에서만 부재 rawcode=0; " + scope + ".");
        }
        if (sameImage) limits.Add("동일 파일의 반복 관측입니다. 메모리 보호 상태는 실행 중에도 달라질 수 있으므로, 차이를 워크 업데이트나 구조 변경으로 단정하지 않습니다.");
        return new(1, before.CaptureId, after.CaptureId, b.FileVersion ?? "Unknown", a.FileVersion ?? "Unknown", sameImage, sameProcess, differences.ToArray(), limits.ToArray());
    }

    private static bool SameToken(string? left, string? right) => left is not null && right is not null && Ordinal.Equals(left, right);
    internal static string Rvas(IEnumerable<uint> values) => string.Join(", ", values.OrderBy(x => x).Select(X));
    private static string Sections(IEnumerable<SectionInfo> sections) => string.Join("; ", sections.Select(s => $"RVA={X(s.Rva)} virtual={s.VirtualSize} rawOffset={X(s.RawOffset)} rawSize={s.RawSize} flags={X(s.Characteristics)} SHA={s.Sha256.ToUpperInvariant()}").OrderBy(x => x, Ordinal));
    private static string Checks(IEnumerable<CodeCheck> checks) => string.Join("; ", checks.Select(c => $"RVA={X(c.Rva)} size={c.Size} Readability/Hash={c.Status} SHA={c.Sha256 ?? "Unknown"}").OrderBy(x => x, Ordinal));
    private static string DistributionValue((int Count, ulong Bytes) value) => $"regions={value.Count}; bytes={value.Bytes}";
    internal static bool IsReadable(RegionInfo region) => region.State == 0x1000 && (region.Protection & 0x100) == 0 && (region.Protection & 0xff) is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80;
    internal static bool IsNoAccess(RegionInfo region) => (region.Protection & 0xff) == 0x01;
    internal static SortedDictionary<string, (int Count, ulong Bytes)> RegionDistribution(Snapshot snapshot)
    {
        var result = new SortedDictionary<string, (int Count, ulong Bytes)>(Ordinal);
        if (snapshot.Live is null) return result;
        var duplicateNames = snapshot.Image.Sections.GroupBy(s => s.Name, Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(Ordinal);
        void Put(string key, ulong bytes)
        {
            var old = result.GetValueOrDefault(key);
            result[key] = (old.Count + 1, old.Bytes + bytes);
        }
        foreach (var region in snapshot.Live.Regions)
        {
            Put("Protection " + X(region.Protection), region.Size);
            Put("State " + X(region.State), region.Size);
            Put("Type " + X(region.Type), region.Size);
            Put(IsReadable(region) ? "Readability Readable" : "Readability NotReadable", region.Size);
            if (IsNoAccess(region)) Put("Protection NoAccess", region.Size);
            ulong end = (ulong)region.Rva + region.Size;
            var overlapping = snapshot.Image.Sections.Select((s, i) => (Section: s, Index: i)).Where(x => (ulong)x.Section.Rva < end && (ulong)x.Section.Rva + x.Section.VirtualSize > region.Rva).ToArray();
            foreach (var item in overlapping)
            {
                ulong bytes = Math.Min(end, (ulong)item.Section.Rva + item.Section.VirtualSize) - Math.Max((ulong)region.Rva, item.Section.Rva);
                bool duplicate = duplicateNames.Contains(item.Section.Name);
                Put("Section " + item.Section.Name + (duplicate ? " [ambiguous #" + N(item.Index) + "]" : ""), bytes);
            }
            if (overlapping.Length == 0) Put("Section Unmapped/header", region.Size);
        }
        return result;
    }
}
