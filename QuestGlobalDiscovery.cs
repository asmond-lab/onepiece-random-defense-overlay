using System.Diagnostics;

namespace OrandOverlay;

internal static class QuestGlobalDiscovery
{
    internal static Dictionary<string, ulong> Find(RouteQuestMemory memory,
        Func<IEnumerable<(ulong Base, byte[] Buffer)>> chunks, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var names = new HashSet<ulong>();
        foreach (var chunk in BoundedChunks(chunks, watch, token))
        {
            var offset = 0;
            while (offset + 4 <= chunk.Buffer.Length)
            {
                var hit = chunk.Buffer.AsSpan(offset).IndexOf(new byte[] { 0, (byte)'g', (byte)'r', 0 });
                if (hit < 0) break;
                names.Add(chunk.Base + (ulong)(offset + hit + 1));
                offset += hit + 4;
                if (names.Count > 1024) throw new InvalidDataException("퀘스트 변수명 후보 과다");
            }
        }
        var candidates = new Dictionary<ulong, Dictionary<string, ulong>>();
        foreach (var chunk in BoundedChunks(chunks, watch, token))
        {
            for (var index = (int)((8 - chunk.Base % 8) % 8); index + 24 <= chunk.Buffer.Length; index += 8)
            {
                if (!names.Contains(BitConverter.ToUInt64(chunk.Buffer, index)) ||
                    BitConverter.ToInt32(chunk.Buffer, index + 8) != 13 ||
                    BitConverter.ToInt32(chunk.Buffer, index + 12) != 13) continue;
                var node = chunk.Base + (ulong)index - 40;
                if (candidates.ContainsKey(node)) continue;
                var table = Walk(memory, node, token);
                if (table is not null) candidates[node] = table;
                if (candidates.Count > 1) throw new InvalidDataException("퀘스트 변수 표 후보가 둘 이상");
            }
        }
        return candidates.Count == 1 ? candidates.Values.Single() : throw new InvalidDataException("퀘스트 변수 표 탐색 중");
    }

    private static Dictionary<string, ulong>? Walk(RouteQuestMemory memory, ulong start, CancellationToken token)
    {
        var nodes = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var visited = new HashSet<ulong>();
        var queue = new Queue<ulong>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var address) && visited.Count < 10000)
        {
            token.ThrowIfCancellationRequested();
            if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(address) || !visited.Add(address)) continue;
            byte[] bytes;
            try { bytes = memory.Bytes(address, 64); }
            catch (InvalidDataException) { continue; }
            var type = BitConverter.ToInt32(bytes, 48);
            if (type is < 0 or > 13 || type != BitConverter.ToInt32(bytes, 52)) continue;
            var previous = BitConverter.ToUInt64(bytes, 24);
            if (previous >= 24) queue.Enqueue(previous - 24);
            queue.Enqueue(BitConverter.ToUInt64(bytes, 32));
            var name = memory.Name(BitConverter.ToUInt64(bytes, 40));
            if (!RouteQuestMemory.RequiredNames.Contains(name, StringComparer.Ordinal)) continue;
            if (!nodes.TryAdd(name, address)) return null;
            if (nodes.Count == RouteQuestMemory.RequiredNames.Length) return nodes;
        }
        return null;
    }

    private static IEnumerable<(ulong Base, byte[] Buffer)> BoundedChunks(
        Func<IEnumerable<(ulong Base, byte[] Buffer)>> chunks, Stopwatch watch, CancellationToken token)
    {
        long bytes = 0;
        foreach (var chunk in chunks())
        {
            token.ThrowIfCancellationRequested();
            bytes += chunk.Buffer.Length;
            if (bytes > 4L * 1024 * 1024 * 1024 || watch.Elapsed > TimeSpan.FromSeconds(12))
                throw new InvalidDataException("퀘스트 탐색 예산 초과 · 다음 시도 대기");
            yield return chunk;
        }
    }
}
