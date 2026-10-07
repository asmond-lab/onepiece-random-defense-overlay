using System.Text;
using System.Text.Json;
using WarcraftProbe;

internal static class Program
{
    internal const string Version = "0.1.0";
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = false, MaxDepth = 32 };
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            if (args.Length == 0) return Interactive();
            if (args.Length == 1 && args[0] == "--version") { Console.WriteLine("WarcraftProbe " + Version + " | read/query only"); return 0; }
            if (args.Length == 1 && args[0] is "--help" or "help") { Help(); return 0; }
            return Execute(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("중단: " + ErrorText(ex));
            return ex is OperationCanceledException ? 3 : 2;
        }
    }
    internal static string ErrorText(Exception e) => e switch
    {
        OperationCanceledException => "취소 또는 시간 제한. 현재 값으로 인정하지 않습니다.",
        UnauthorizedAccessException => "허용되지 않은 파일 또는 읽기 범위입니다.",
        IOException => "파일/대상 상태 또는 읽기 검증을 확인하세요. 기존 출력은 덮어쓰지 않습니다.",
        ArgumentException => "명령이나 입력 경로가 올바르지 않습니다. --help를 확인하세요.",
        _ => "검증 실패 (" + e.GetType().Name + "). 게임 설정이나 보호 상태는 변경하지 않았습니다."
    };
    internal static Dictionary<string,string> Options(string[] args, string[] allowed, string[] flags)
    {
        var result = new Dictionary<string,string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Length; i++)
        {
            var key = args[i];
            if (!allowed.Contains(key, StringComparer.Ordinal) || result.ContainsKey(key)) throw new ArgumentException("Unknown/repeated option");
            if (flags.Contains(key, StringComparer.Ordinal)) result.Add(key, "true");
            else { if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Missing option value"); result.Add(key, args[i]); }
        }
        return result;
    }
    internal static string Required(Dictionary<string,string> options, string key) => options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Missing option");
    internal static string Label(string text)
    {
        if (text.Length is < 1 or > 80 || text.Any(char.IsControl)) throw new ArgumentException("Invalid label");
        return text;
    }
    internal static int Execute(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException();
        if (args[0] == "capture")
        {
            var o = Options(args, ["--copy", "--out", "--label", "--pid", "--started-at", "--metadata-only"], ["--metadata-only"]);
            var copy = Required(o, "--copy"); var output = Required(o, "--out"); var label = Label(o.GetValueOrDefault("--label", "워크 진단"));
            if (o.ContainsKey("--pid") != o.ContainsKey("--started-at")) throw new ArgumentException("PID/start must be paired");
            int? pid = null; DateTimeOffset? start = null;
            if (o.ContainsKey("--pid"))
            {
                if (!int.TryParse(o["--pid"], out int p) || p <= 0 || !DateTimeOffset.TryParse(o["--started-at"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var s)) throw new ArgumentException("Invalid identity");
                pid = p; start = s.ToUniversalTime();
            }
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cts.Cancel(); }; Console.CancelKeyPress += cancel;
            Snapshot snap;
            try { snap = WindowsCollector.Capture(copy, label, pid, start, !o.ContainsKey("--metadata-only"), cts.Token); }
            finally { Console.CancelKeyPress -= cancel; }
            SnapshotDiff.Validate(snap);
            Save(output, "snapshot.json", snap, ReportWriter.SnapshotMarkdown(snap));
            Console.WriteLine("스냅샷 저장 완료. 자동 인식 승인이나 게임 전체 덤프가 아닙니다."); return 0;
        }
        if (args[0] == "inspect")
        {
            var o = Options(args, ["--copy", "--out", "--label"], []);
            var label = Label(o.GetValueOrDefault("--label", "파일 기준선")); var started = DateTimeOffset.UtcNow;
            var copy = Required(o, "--copy"); var bytes = ProbeFiles.ReadLocalFile(copy, 256L * 1024 * 1024);
            var image = PeInspector.Analyze(bytes, Path.GetFileName(copy));
            var snap = new Snapshot(1, Version, Guid.NewGuid().ToString("N"), "offline", label, started, DateTimeOffset.UtcNow, image, null);
            SnapshotDiff.Validate(snap); Save(Required(o, "--out"), "snapshot.json", snap, ReportWriter.SnapshotMarkdown(snap));
            Console.WriteLine("파일 기준선 저장 완료. 실행 중 게임의 상태는 수집하지 않았습니다."); return 0;
        }
        if (args[0] == "compare")
        {
            var o = Options(args, ["--before", "--after", "--out"], []);
            Snapshot Load(string file)
            {
                var bytes = ProbeFiles.ReadLocalFile(file, 16L * 1024 * 1024);
                var s = JsonSerializer.Deserialize<Snapshot>(bytes, Json) ?? throw new InvalidDataException("Empty snapshot");
                SnapshotDiff.Validate(s); return s;
            }
            var comparison = SnapshotDiff.Compare(Load(Required(o, "--before")), Load(Required(o, "--after")));
            Save(Required(o, "--out"), "comparison.json", comparison, ReportWriter.ComparisonMarkdown(comparison));
            Console.WriteLine("비교 보고서 저장 완료. 변경 후보는 사람이 검증해야 합니다."); return 0;
        }
        throw new ArgumentException("Unknown command");
    }
    private static void Save<T>(string output, string name, T document, string markdown)
    {
        ProbeFiles.SaveNew(output, name, JsonSerializer.SerializeToUtf8Bytes(document, Json), Encoding.UTF8.GetBytes(markdown));
        Console.WriteLine(Path.GetFullPath(output));
    }
    private static int Interactive()
    {
        Console.WriteLine("WarcraftProbe " + Version + " | 워크 업데이트 비교 도구");
        Console.WriteLine("""
1. 현재 워크 스냅샷
2. 복사해 둔 EXE 기준선
3. 두 스냅샷 비교
Enter: 종료
게임 입력·메모리 쓰기·보호 변경은 하지 않습니다.
""");
        var choice = Console.ReadLine(); if (string.IsNullOrWhiteSpace(choice)) return 0;
        string Ask(string message) { Console.WriteLine(message); return (Console.ReadLine() ?? "").Trim().Trim('"'); }
        var output = Path.Combine(AppContext.BaseDirectory, "Captures", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        string[] command = choice switch
        {
            "1" => ["capture", "--copy", Ask("분석용으로 복사해 둔 Warcraft III.exe 경로:"), "--label", Ask("샘플 이름 (예: 업데이트 전 / 조합 후):"), "--out", output],
            "2" => ["inspect", "--copy", Ask("분석용 EXE 복사본 경로:"), "--label", Ask("기준선 이름:"), "--out", output],
            "3" => ["compare", "--before", Ask("이전 snapshot.json 경로:"), "--after", Ask("이후 snapshot.json 경로:"), "--out", output],
            _ => throw new ArgumentException("Invalid menu")
        };
        int result; try { result = Execute(command); } catch(Exception e) { Console.WriteLine("중단: " + ErrorText(e)); result = 2; }
        Console.WriteLine("Enter를 누르면 종료합니다."); Console.ReadLine(); return result;
    }
    private static void Help() => Console.WriteLine("""
WarcraftProbe 0.1.0
capture --copy <분석용 EXE 복사본> --out <새 폴더> [--label <이름>] [--pid <번호> --started-at <UTC 시각>] [--metadata-only]
inspect --copy <분석용 EXE 복사본> --out <새 폴더> [--label <이름>]
compare --before <이전 snapshot.json> --after <이후 snapshot.json> --out <새 폴더>

원본 설치 EXE 대신 별도 복사본을 지정하세요. 현재 실행 파일과 동일 경로는 수집에서 거부합니다.
새 버전은 파일 구조·페이지 접근 상태를 우선 기록하며, 기존 버전의 객체 위치를 추측해서 적용하지 않습니다.
전체 메모리 덤프·게임 입력·메모리 쓰기·보호 변경·자동 프로필 승인은 하지 않습니다.
""");
}
