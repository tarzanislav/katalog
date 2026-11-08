namespace Katalog.Vystupy;

/// <summary>Outputs of the program; each takes its selection as a query of the one query language (decision A4).</summary>
public enum DruhVystupu
{
    /// <summary>Static web catalogue, encrypted with a password (stage E9).</summary>
    Web = 1,
    Csv = 2,
    Html = 3,
    /// <summary>Labels with QR codes for disks and covers (stage E6).</summary>
    Stitky = 4,
    /// <summary>Playlist for players (stage E7).</summary>
    M3u = 5,
}
