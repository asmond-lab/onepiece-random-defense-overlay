using System.Text;

namespace OrandOverlay;

internal sealed class RouteQuestMemory(Func<ulong, int, byte[]> read)
{
    internal static readonly (string Id, string Active, string Index)[] Bindings =
        "Dy Oy Qy Yy iy vy Gr Er wr Mr Sr Xr cr Br ar dr gr yr rr mr sr Hr hr tr pr Wr Cr fr xr Kr Ir Vr Zr Dr"
            .Split(' ').Chunk(2).Select((pair, i) => ($"Q{i:000}", pair[0], pair[1])).ToArray();
    internal static readonly string[] RequiredNames = Bindings.SelectMany(x => new[] { x.Active, x.Index })
        .Concat(new[] { "Sh", "Mh" }).ToArray();

    internal byte[] Bytes(ulong address, int length)
    {
        if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(address)) throw new InvalidDataException("퀘스트 포인터 범위 오류");
        var bytes = read(address, length);
        if (bytes.Length != length) throw new InvalidDataException("퀘스트 메모리 읽기 실패");
        return bytes;
    }

    internal string Name(ulong address)
    {
        var bytes = read(address, 32);
        var end = System.Array.IndexOf(bytes, (byte)0);
        return end is > 0 and < 32 ? Encoding.ASCII.GetString(bytes, 0, end) : "";
    }

    internal byte[] Node(ulong address, string name, int type)
    {
        var bytes = Bytes(address, 64);
        if (BitConverter.ToInt32(bytes, 48) != type || BitConverter.ToInt32(bytes, 52) != type ||
            Name(BitConverter.ToUInt64(bytes, 40)) != name)
            throw new InvalidDataException("퀘스트 변수 표 구조 변경");
        return bytes;
    }

    internal int Scalar(ulong node, string name) => BitConverter.ToInt32(Node(node, name, 4), 56);

    internal int[]? Array(ulong node, string name, int type, int maximum)
    {
        var identity = Node(node, name, type);
        var pointer = BitConverter.ToUInt64(identity, 56);
        if (pointer == 0)
        {
            if (!identity.AsSpan().SequenceEqual(Node(node, name, type)))
                throw new InvalidDataException("퀘스트 배열 포인터 변경 중");
            return null;
        }
        var header = Bytes(pointer, 32);
        var length = BitConverter.ToInt32(header, 8);
        var capacity = BitConverter.ToInt32(header, 24);
        if (length < 1 || length > maximum || capacity < length || capacity > Math.Max(32, maximum * 2))
            throw new InvalidDataException($"퀘스트 배열 크기 미지원 ({name}: {length}/{capacity})");
        var data = BitConverter.ToUInt64(header, 16);
        var bytes = Bytes(data, length * 4);
        if (!bytes.AsSpan().SequenceEqual(Bytes(data, length * 4)) ||
            !header.AsSpan().SequenceEqual(Bytes(pointer, 32)) ||
            !identity.AsSpan().SequenceEqual(Node(node, name, type)))
            throw new InvalidDataException("퀘스트 배열 상태 변경 중");
        return Enumerable.Range(0, length).Select(i => BitConverter.ToInt32(bytes, i * 4)).ToArray();
    }

    internal static string Rawcode(int value) => new(new[] { (char)((value >> 24) & 255),
        (char)((value >> 16) & 255), (char)((value >> 8) & 255), (char)(value & 255) });
}
