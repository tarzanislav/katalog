using System.Text.Json;
using Katalog.Jadro.Zdravie;
using Katalog.Zdravie;

// Katalog.Zdravie: reads the health of the physical disks (SMART of ATA/SATA, the health log of NVMe; USB disks when
// their enclosure passes SMART through) and writes it as JSON – to standard output, or with --vystup to a new file
// (the main program starts this helper with administrator rights, which cannot pass standard output back).
// Exit codes: 0 done, 2 wrong arguments, 3 the output file could not be made.

string? vystup = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--json" or "-j":
            break;
        case "--vystup" when i + 1 < args.Length:
            vystup = args[++i];
            break;
        default:
            Console.Error.WriteLine("Použitie: Katalog.Zdravie.exe [--json] [--vystup <nový súbor .json>]");
            return 2;
    }
}

if (vystup is not null && (!Path.IsPathFullyQualified(vystup) || !vystup.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                           !Directory.Exists(Path.GetDirectoryName(vystup))))
{
    Console.Error.WriteLine("Výstup musí byť úplná cesta k novému súboru .json v existujúcom priečinku.");
    return 2;
}

var vysledok = new VystupZdravia(1, OperatingSystem.IsWindows() ? CitanieDiskov.Vsetky() : []);
var json = JsonSerializer.SerializeToUtf8Bytes(vysledok);
if (vystup is null)
{
    using var konzola = Console.OpenStandardOutput();
    konzola.Write(json);
    return 0;
}

try
{
    // a new file only: running with administrator rights, it never overwrites anything (not even through a link)
    using var subor = new FileStream(vystup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    subor.Write(json);
    return 0;
}
catch (IOException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 3;
}
