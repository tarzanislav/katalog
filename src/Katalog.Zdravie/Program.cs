using System.Text.Json;

// Katalog.Zdravie: reads the health of disks (SMART) and writes it as JSON to its standard output.
// Stage E0 has only the frame; reading SMART of ATA, NVMe and USB disks comes in stage E5.
// Output: {"verzia":1,"disky":[]}, exit code 0; an unknown argument gives exit code 2.

if (args.Length > 0 && args[0] is not ("--json" or "-j"))
{
    Console.Error.WriteLine("Použitie: Katalog.Zdravie.exe [--json]");
    return 2;
}

var vystup = new { verzia = 1, disky = Array.Empty<object>() };
Console.WriteLine(JsonSerializer.Serialize(vystup));
return 0;
