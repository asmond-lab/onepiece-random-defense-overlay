namespace OrandOverlay;

/// <summary>
/// ORDR의 네 인간 플레이어 슬롯만 로컬 소유자로 인정한다.
/// 루트와 ID를 두 번 읽어 한 스냅샷에서 바뀐 값은 읽기 race로 폐기한다.
/// </summary>
public static class LocalPlayerSlotResolver
{
    public const byte FirstHumanSlot = 0;
    public const byte LastHumanSlot = 3;

    public static byte? Resolve(ulong rootBefore, ushort slotBefore,
        ulong rootAfter, ushort slotAfter)
    {
        if (rootBefore == 0 || rootBefore != rootAfter || slotBefore != slotAfter)
            return null;
        return slotBefore is >= FirstHumanSlot and <= LastHumanSlot
            ? (byte)slotBefore
            : null;
    }
}
