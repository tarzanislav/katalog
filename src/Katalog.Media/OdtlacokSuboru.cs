using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Katalog.Media;

/// <summary>
/// The fingerprint of a file for finding exact copies (duplicates, stage E5): SHA-256 of its size, its first and its last
/// megabyte, shortened to 16 bytes. Two megabytes are read however big the file is, so even a 50 GB remux takes a moment;
/// two different files of the same size almost never share both ends.
/// </summary>
public static class OdtlacokSuboru
{
    public const int Dlzka = 16;
    private const int Kus = 1 << 20;

    /// <summary>The fingerprint of a file of <paramref name="velkost"/> bytes; null when it cannot be read or its size changed.</summary>
    public static byte[]? Vypocitaj(string cesta, long velkost)
    {
        try
        {
            using var subor = new FileStream(cesta, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.RandomAccess,
                BufferSize = 0,
            });
            if (subor.Length != velkost)
            {
                return null;
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> dlzka = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(dlzka, velkost);
            hash.AppendData(dlzka);
            var buffer = new byte[Kus];
            hash.AppendData(buffer, 0, Citaj(subor, buffer, 0));
            if (velkost > Kus)
            {
                hash.AppendData(buffer, 0, Citaj(subor, buffer, Math.Max(Kus, velkost - Kus)));
            }

            return hash.GetHashAndReset()[..Dlzka];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int Citaj(FileStream subor, byte[] buffer, long od)
    {
        subor.Position = od;
        var spolu = 0;
        int precitane;
        while (spolu < buffer.Length && (precitane = subor.Read(buffer, spolu, buffer.Length - spolu)) > 0)
        {
            spolu += precitane;
        }

        return spolu;
    }
}
