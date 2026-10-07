using System.IO;
using System.Globalization;
using System.Text.Json;
using OrandOverlay;

internal static class InventoryTraceCommand
{
    private const string Help = "사용법: RandyPickLiveValidation record-inventory --pid <PID> --started-at <UTC ISO Z> --authorized-exe-copy <검증된 복사본> --map-version <2.320|2.322|2.323> --output <새 로컬 JSON 경로>\n" +
        "       RandyPickLiveValidation replay-inventory --input <로컬 JSON 경로> [--session <캡처 세션키>]\n" +
        "기록은 명시적 1회, 최대 49152회/1MiB/5초입니다. 저장된 과거 기록은 게임이 종료되거나 새 게임을 시작해도 오프라인 재생할 수 있습니다. " +
        "새 게임 세션의 현재 상태를 확인하려면 새 기록과 독립적인 확인이 필요합니다. 재생은 프로세스/사용자 프로필/네트워크에 접근하지 않습니다. 출력은 독립적인 게임 정답이 아닙니다.";
    internal static int Run(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        if (args[0] == "--help") { Console.WriteLine(Help); return 0; }
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
                if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal) ||
                    !values.TryAdd(args[i], args[i + 1])) throw new ArgumentException("인수가 올바르지 않습니다.");
            var profile = JsonSerializer.Deserialize<MemoryProfile[]>(Program.ProfileJson)!.Single();
            if (args[0] == "replay-inventory")
            {
                if (!values.ContainsKey("--input") || values.Keys.Except(new[] { "--input", "--session" }).Any()) throw new ArgumentException();
                using var input = new FileStream(values["--input"], FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length is 0 or > InventoryReadTrace.MaximumFileBytes) throw new InvalidDataException("Trace file cap");
                var file = new byte[checked((int)input.Length)];
                input.ReadExactly(file);
                if (input.ReadByte() != -1) throw new InvalidDataException("Trace grew during read");
                var trace = InventoryReadTrace.Decode(file, profile);
                if (values.TryGetValue("--session", out var expected) && trace.Identity.Session != expected)
                    throw new InvalidDataException("Trace session mismatch");
                var result = InventoryReadTrace.Replay(file, profile, trace.Identity);
                Report(result, "offline-replay", trace.Identity.Session);
                return result.Failure is null ? 0 : 1;
            }
            if (args[0] != "record-inventory" || values.Count != 5 ||
                values.Keys.Except(new[] { "--pid", "--started-at", "--authorized-exe-copy", "--map-version", "--output" }).Any() ||
                !int.TryParse(values["--pid"], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid < 1 ||
                !DateTimeOffset.TryParseExact(values["--started-at"],
                    new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" },
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var started) ||
                !values["--started-at"].EndsWith("Z", StringComparison.Ordinal)) throw new ArgumentException();
            var target = new ExpectedReadTarget(pid, started);
            BoundReadSession.Metadata(target); // Exact process identity before native handle or copy access.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var binding = BoundReadSession.Open(target, values["--authorized-exe-copy"], timeout.Token);
            binding.Revalidate();
            var map = values["--map-version"];
            var mapHash = map switch { "2.320" => Map2320GrowthSource.JassSha256,
                "2.322" => Map2322SourceContract.JassSha256, "2.323" => Map2323SourceContract.JassSha256,
                _ => throw new ArgumentException("지원되지 않는 맵 버전") };
            var id = new InventoryReadTrace.Identity(Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
                map, mapHash, profile.ProfileId, profile.ProfileRevision, InventoryReadTrace.ProfileHash(profile),
                binding.SessionKey, binding.Module);
            var (fileBytes, capture) = InventoryReadTrace.Capture(binding.Read, id, profile);
            binding.Revalidate();
            if (!capture.Complete) throw new InvalidDataException("캡처 한도 초과: 불완전한 기록은 저장하지 않습니다.");
            var path = Path.GetFullPath(values["--output"]);
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) output.Write(fileBytes);
            Report(capture, "captured-unlabeled", id.Session);
            return capture.Failure is null ? 0 : 1;
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException)
        {
            Console.Error.WriteLine($"인벤토리 기록/재생 실패: {e.GetType().Name}: {e.Message}\n{Help}");
            return 2;
        }
    }
    private static void Report(InventoryReadTrace.Result result, string evidence, string session) =>
        Console.WriteLine(JsonSerializer.Serialize(new {
            Evidence = evidence, Session = session, result.Complete, result.Calls, result.Bytes, result.ElapsedMilliseconds,
            result.Failure, FrameCount = result.Inventory?.Count, Owned = result.Inventory?.Owned,
            Foreign = result.Inventory?.Foreign, Rawcodes = result.Inventory?.Rawcodes,
            Verified = false, AllowsReader = false, GameplayReady = false
        }));
}
