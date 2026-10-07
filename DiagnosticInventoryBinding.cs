using System.Security.Cryptography;
using System.Text;

namespace OrandOverlay;

internal static class DiagnosticInventoryBinding
{
    internal static string Context(string salt, string session,
        Warcraft300WorldLocator.Context locator, Warcraft300Diagnostic.View view) => Hash(writer =>
    {
        writer.Write(salt); writer.Write(session);
        writer.Write(locator.K); writer.Write(locator.KeyA); writer.Write(locator.KeyB);
        writer.Write(locator.EncodedUi); writer.Write(locator.EncodedWorld);
        writer.Write(locator.ByteA); writer.Write(locator.ByteB); writer.Write(locator.Ui); writer.Write(locator.World);
        WriteView(writer, view);
    });

    internal static string World(string salt, Warcraft300Diagnostic.Inventory inventory) => Hash(writer =>
    {
        writer.Write(salt);
        WriteView(writer, inventory.CurrentView);
        writer.Write(inventory.Count); writer.Write(inventory.Owned); writer.Write(inventory.Foreign);
        writer.Write(inventory.EntryAddresses.Count);
        foreach (var address in inventory.EntryAddresses) writer.Write(address);
        writer.Write(inventory.Units.Count);
        foreach (var unit in inventory.Units)
        {
            writer.Write(unit.Address); writer.Write(unit.Owner); writer.Write(unit.Rawcode);
            WriteAllocation(writer, unit.Allocation);
        }
        writer.Write(inventory.UnregisteredEntries.Count);
        foreach (var entry in inventory.UnregisteredEntries)
        {
            writer.Write(entry.Index); writer.Write(entry.Address); writer.Write(entry.Vtable);
            writer.Write(entry.Handle); writer.Write(entry.Serial);
        }
    });

    // Membership proof for one already validated neutral growth candidate, not QR attribution.
    // Physical identity is view + address + owner + rawcode. Handle/allocation stamps churn
    // with unrelated world objects and must not drop the attributed card. Address keeps
    // another player's owner-27 special from substituting.
    internal static string GrowthUnit(string salt, Warcraft300Diagnostic.View view,
        Warcraft300Diagnostic.Unit unit) => Hash(writer =>
    {
        writer.Write(salt); writer.Write("growth-unit");
        WriteView(writer, view);
        writer.Write(unit.Address); writer.Write(unit.Owner); writer.Write(unit.Rawcode);
    });

    private static void WriteView(BinaryWriter writer, Warcraft300Diagnostic.View view)
    { writer.Write(view.Root); writer.Write(view.Slot); writer.Write(view.Player); }

    private static void WriteAllocation(BinaryWriter writer, Warcraft300HandleStamp value)
    {
        writer.Write(value.ModuleBase); writer.Write(value.Unit); writer.Write(value.Vtable);
        writer.Write(value.RegistryGlobal); writer.Write(value.Registry); writer.Write(value.RawHandle);
        writer.Write(value.Serial); writer.Write(value.Table); writer.Write(value.TableLimit);
        writer.Write(value.Slot); writer.Write(value.Marker); writer.Write(value.Record);
        writer.Write(value.RecordSerial); writer.Write(value.TypeId); writer.Write(value.BackReference);
        writer.Write(value.State30); writer.Write(value.State83);
    }

    private static string Hash(Action<BinaryWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true)) write(writer);
        return Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))));
    }
}
