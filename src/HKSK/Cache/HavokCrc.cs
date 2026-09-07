using System.Globalization;

namespace HKSK.Cache;

/// <summary>
/// The checksum the animation set data uses to name animation files.
/// </summary>
/// <remarks>
/// A CRC-32 over the lowercased text, polynomial 0x04C11DB7, reflected in and
/// out, with no initial value and no final xor. That is not the common zlib
/// variant, which starts at 0xFFFFFFFF and inverts the result, so a stock
/// CRC-32 will not reproduce these numbers.
///
/// Each animation contributes three lines: the checksum of its folder, the
/// checksum of its bare filename, and <see cref="ExtensionCode"/>.
///
/// Verified against the shipped set data -- for example the chicken's
/// <c>Animations\AggroWarning1.hkx</c> gives folder 2725300844 and name
/// 329189360, which is what <c>chickenprojectdata/fullbody.txt</c> lists.
/// </remarks>
public static class HavokCrc
{
    /// <summary>
    /// The third line of every CRC triple: 7891816, which is 0x786B68 -- the
    /// bytes of "hkx" in reverse. It is a constant for animation files, not a
    /// checksum of anything, and the game writes it verbatim.
    /// </summary>
    public const string ExtensionCode = "7891816";

    private const uint Polynomial = 0x04C11DB7;

    /// <summary>Checksums text the way the set data does, lowercasing it first.</summary>
    public static uint Compute(string text)
    {
        uint crc = 0;

        foreach (char ch in text.ToLowerInvariant())
        {
            uint c = ReflectByte((byte)ch);

            for (int i = 0; i < 8; i++)
            {
                uint bit = crc >> 31;
                if ((c & 0x80) != 0) bit ^= 1;
                c = (c << 1) & 0xFF;

                crc <<= 1;
                if (bit != 0) crc ^= Polynomial;
            }
        }

        return Reflect32(crc);
    }

    /// <summary>The checksum as the set data writes it: unsigned decimal.</summary>
    public static string Text(string value) =>
        Compute(value).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The CRC triple for one animation, given its path relative to the data
    /// folder, e.g. <c>meshes\actors\ambient\chicken\animations\aggrowarning1.hkx</c>.
    /// </summary>
    public static (string Folder, string Name, string Extension) Triple(string relativePath)
    {
        string normalised = relativePath.Replace('/', '\\');

        int slash = normalised.LastIndexOf('\\');
        string folder = slash < 0 ? "" : normalised[..slash];
        string name = slash < 0 ? normalised : normalised[(slash + 1)..];

        int dot = name.LastIndexOf('.');
        if (dot >= 0) name = name[..dot];

        return (Text(folder), Text(name), ExtensionCode);
    }

    private static uint ReflectByte(byte value)
    {
        uint output = 0;
        for (int i = 0; i < 8; i++)
            if ((value & (1 << i)) != 0)
                output |= 1u << (7 - i);
        return output;
    }

    private static uint Reflect32(uint value)
    {
        uint output = 0;
        for (int i = 0; i < 32; i++)
            if ((value & (1u << i)) != 0)
                output |= 1u << (31 - i);
        return output;
    }
}
