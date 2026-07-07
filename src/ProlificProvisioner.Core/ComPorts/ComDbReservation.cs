using Microsoft.Win32;
using System.Runtime.Versioning;

namespace ProlificProvisioner.Core.ComPorts;

/// <summary>
/// Reads/writes the COM Name Arbiter's port-reservation bitmap
/// (HKLM\SYSTEM\CurrentControlSet\Control\COM Name Arbiter\ComDB), so a COM number
/// we force onto a device won't later get silently handed out to something else,
/// and so we can free a stale reservation before re-forcing the same number.
/// Bit (N-1) of the byte array corresponds to COM N.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ComDbReservation
{
    private const string ComNameArbiterPath = @"SYSTEM\CurrentControlSet\Control\COM Name Arbiter";
    private const int MaxComPorts = 4096; // ComDB supports COM1..COM4096

    public static bool IsReserved(int comNumber)
    {
        var bitmap = ReadBitmap();
        return GetBit(bitmap, comNumber);
    }

    public static void Reserve(int comNumber)
    {
        var bitmap = ReadBitmap();
        SetBit(bitmap, comNumber, true);
        WriteBitmap(bitmap);
    }

    public static void Release(int comNumber)
    {
        var bitmap = ReadBitmap();
        SetBit(bitmap, comNumber, false);
        WriteBitmap(bitmap);
    }

    private static byte[] ReadBitmap()
    {
        using var key = Registry.LocalMachine.OpenSubKey(ComNameArbiterPath, writable: false);
        var existing = key?.GetValue("ComDB") as byte[];
        if (existing is { Length: > 0 })
        {
            return existing;
        }

        return new byte[MaxComPorts / 8];
    }

    private static void WriteBitmap(byte[] bitmap)
    {
        using var key = Registry.LocalMachine.CreateSubKey(ComNameArbiterPath, writable: true)
            ?? throw new InvalidOperationException("Could not open COM Name Arbiter key. Is the app running elevated?");
        key.SetValue("ComDB", bitmap, RegistryValueKind.Binary);
    }

    private static bool GetBit(byte[] bitmap, int comNumber)
    {
        var (byteIndex, bitIndex) = Locate(comNumber, bitmap.Length);
        return (bitmap[byteIndex] & (1 << bitIndex)) != 0;
    }

    private static void SetBit(byte[] bitmap, int comNumber, bool value)
    {
        var (byteIndex, bitIndex) = Locate(comNumber, bitmap.Length);
        if (value)
        {
            bitmap[byteIndex] |= (byte)(1 << bitIndex);
        }
        else
        {
            bitmap[byteIndex] &= (byte)~(1 << bitIndex);
        }
    }

    private static (int byteIndex, int bitIndex) Locate(int comNumber, int bitmapLength)
    {
        var zeroBased = comNumber - 1;
        var byteIndex = zeroBased / 8;
        if (byteIndex >= bitmapLength)
        {
            throw new ArgumentOutOfRangeException(nameof(comNumber), $"COM{comNumber} is outside the ComDB bitmap range.");
        }

        return (byteIndex, zeroBased % 8);
    }
}
