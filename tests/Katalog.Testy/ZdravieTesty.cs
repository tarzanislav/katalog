using System.Text.Json;
using Katalog.Data;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Jadro.Zdravie;

namespace Katalog.Testy;

/// <summary>Reading SMART answers (invented bytes, as ATA and NVMe disks give them) and matching them to the library's disks.</summary>
public sealed class ZdravieTesty
{
    [Test]
    public void AtaAtributy()
    {
        var atributy = new byte[512];
        var prahy = new byte[512];
        void Atribut(int i, byte id, byte hodnota, long surova, byte prah)
        {
            var od = 2 + (i * 12);
            atributy[od] = id;
            atributy[od + 3] = hodnota;
            atributy[od + 4] = hodnota;
            for (var b = 0; b < 6; b++)
            {
                atributy[od + 5 + b] = (byte)(surova >> (8 * b));
            }

            prahy[od] = id;
            prahy[od + 1] = prah;
        }

        Atribut(0, 5, 100, 0, 36);          // reallocated
        Atribut(1, 9, 99, 12_345, 0);       // power-on hours
        Atribut(2, 194, 64, 0x0000_1E00_0024, 0); // temperature 36 °C (low byte), min/max in the other bytes
        Atribut(3, 197, 100, 0, 0);
        var zoznam = Smart.CitajAta(atributy, prahy);
        Over.Rovne(4, zoznam.Count, "štyri atribúty");
        var dobry = Smart.VyhodnotAta(0, "Vymyslený HDD", "VYM123", "SATA", zoznam);
        Over.Pravda(dobry is { Stav: Smart.Dobry, Hodiny: 12_345, Teplota: 36, Realokovane: 0, Dovod: null }, $"dobrý disk: {dobry}");

        Atribut(0, 5, 100, 8, 36);
        var varovanie = Smart.VyhodnotAta(0, null, null, "SATA", Smart.CitajAta(atributy, prahy));
        Over.Pravda(varovanie is { Stav: Smart.Varovanie, Realokovane: 8 } && varovanie.Dovod!.Contains("presunuté sektory: 8", StringComparison.Ordinal), "presunuté sektory sú varovanie");

        Atribut(0, 5, 30, 400, 36);
        var zly = Smart.VyhodnotAta(0, null, null, "SATA", Smart.CitajAta(atributy, prahy));
        Over.Pravda(zly is { Stav: Smart.Zly } && zly.Dovod!.Contains("atribút 5", StringComparison.Ordinal), "atribút pod prahom je zlyhanie");

        Atribut(0, 231, 7, 0, 0);
        Over.Rovne<int?>(93, Smart.VyhodnotAta(0, null, null, "SATA", Smart.CitajAta(atributy, [])).Opotrebenie, "opotrebenie SSD (zostáva 7 %)");
    }

    [Test]
    public void NvmeZaznam()
    {
        var log = new byte[512];
        BitConverter.GetBytes((ushort)(273 + 41)).CopyTo(log, 1);
        log[3] = 100;
        log[4] = 10;
        log[5] = 3;
        BitConverter.GetBytes(4_321UL).CopyTo(log, 128);
        var dobry = Smart.VyhodnotNvme(1, "Vymyslený NVMe", "NVM1", log);
        Over.Pravda(dobry is { Stav: Smart.Dobry, Teplota: 41, Hodiny: 4_321, Opotrebenie: 3, Neopravitelne: 0, Zbernica: "NVMe" }, $"dobrý NVMe: {dobry}");

        BitConverter.GetBytes(2UL).CopyTo(log, 160);
        Over.Rovne(Smart.Varovanie, Smart.VyhodnotNvme(1, null, null, log).Stav, "chyby média sú varovanie");
        log[0] = 0x04;
        var zly = Smart.VyhodnotNvme(1, null, null, log);
        Over.Pravda(zly is { Stav: Smart.Zly } && zly.Dovod!.Contains("spoľahlivosť", StringComparison.Ordinal), "znížená spoľahlivosť je zlyhanie");
        Over.Rovne(Smart.Neznamy, Smart.VyhodnotNvme(1, null, null, new byte[10]).Stav, "krátky záznam");
    }

    [Test]
    public void PriradenieDiskom()
    {
        static Disk D(long id, string? seriove) =>
            new(id, 1, (int)id, "Vymyslený " + id, new IdentitaZvazku(DruhDisku.Usb, "AAAA-" + id, null, seriove, null, null, null, null), "", null, null, null, 0, null, null);

        VysledokZdravia[] vysledky =
        [
            new(0, "Model", " VYM123 ", "SATA", 35, 100, 0, 0, 0, null, Smart.Dobry, null, null),
            new(1, "Iný", null, "USB", null, null, null, null, null, null, Smart.Neznamy, null, "Rámček USB disku SMART neposiela ďalej."),
        ];
        var priradene = Prehlady.Prirad([D(1, "vym123"), D(2, "VYM123"), D(3, null), D(4, "INY")], vysledky);
        Over.Postupnost([1L, 2L], priradene.Select(p => p.DiskId), "dva zväzky jedného fyzického disku");
        Over.Rovne(new ZdravieDisku(9, StavZdravia.Dobry, 35, 100, 0, 0, null), Prehlady.ZoVysledku(priradene[0].Zdravie, 9), "záznam");

        var json = JsonSerializer.Serialize(new VystupZdravia(1, vysledky));
        Over.Rovne(vysledky[0], JsonSerializer.Deserialize<VystupZdravia>(json)!.Disky[0], "JSON pomocníka sa prečíta späť");
    }
}
