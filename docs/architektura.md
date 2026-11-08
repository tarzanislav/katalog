# KATALOG – architektúra a dátový model

Stav: etapa E0, 8. 11. 2026. Návrh schválil vlastník (odporúčania R1–R10, návrhy N1–N6, rozhodnutia A1–A10).

## Zásady

- **Jadro bez závislostí**: entity, pravidlá a dotazovací jazyk nevedia nič o WPF, SQLite ani internete.
- **Okno nikdy nečaká**: disky, sieť a databáza idú mimo vlákna okna; priebeh sa hlási najviac 10-krát za sekundu.
- **Jeden zapisovateľ**: do knižnice zapisuje jedna fronta v dávkach; čítať môže naraz veľa vlákien (WAL).
- **Nič sa nestráca**: zmazaný súbor sa len označí s dátumom; pred každou zmenou schémy vznikne číslovaná záloha.
- **Najmenšie oprávnenia**: program beží bez práv správcu; len zdravie diskov číta malý pomocný program, na požiadanie.
- **Vlastný kód**: zdroje údajov, úložiská aj šablóny webu sú súčasťou programu s jednotným rozhraním.
- **Súkromie**: údaje používateľa nikdy neopustia jeho počítač; do repozitára, testov a hlásení chýb nejdú.

## Vrstvy

```
Rozhranie (Katalog.Okno, WPF)
        │
Služby (skenovanie, hľadanie, knižnica, prehrávanie, výstupy, fronta úloh)
        │
Jadro (Katalog.Jadro): entity a kódy, pravidlá, dotazovací jazyk, rozhrania zdrojov, úložísk a výstupov
        ▲
Moduly implementujú rozhrania jadra:
  Data (SQLite, FTS5, migrácie) · Zdroje (TMDB, HISTRA, Jellyfin) · Media (ffprobe, ffmpeg, názvy súborov)
  Úložiská (disky, NAS, DVD/BD, WinSCP) · Windows (pripojenie diskov, DPAPI, SMART)
```

## Dátový model

```
ÚLOŽISKÁ                         MOST                    KNIŽNICA
katalóg                                                  kolekcia ──N:M── titul ── epizóda
  └ disk (identita zväzku)                                                 │          │
      └ priečinok (strom: rodič + meno)                                    │          │
          └ súbor ─────────────── verzia ─── patrí k titulu (film) alebo epizóde
              └ mediálne údaje, stopy, náhľady
```

- **Verzia** spája súbory s titulom (A3): jeden alebo viac súborov jedného filmu či epizódy (CD1 a CD2, VIDEO_TS,
  BDMV). Duplicity sú viac verzií jedného titulu; nezálohovaný titul má všetky verzie na jednom disku.
- **Titul existuje aj bez súborov** (A2): zoznam želaní, chýbajúce diely kolekcií a HISTRA „nemám“.
- **Pozretia** patria titulu či epizóde, nie súboru, takže prežijú výmenu verzie za lepšiu.
- **Identifikátory** zdrojov (TMDB, HISTRA, Jellyfin, IMDb) a **pôvod každého poľa** so zámkom pri ručnej úprave (A5).
- Ďalšie tabuľky: osoby, žánre, štítky, playlisty, uložené hľadania, výpožičky, HISTRA zoznamy, fronta úloh,
  história skenov, zdravie diskov, obrázky (uložené raz podľa obsahu).
- Jeden **fulltextový index** všetkých názvov (FTS5, trojznakové členenie, bez diakritiky, bez ukladania textu dvakrát);
  kľúč riadku je `id * 8 + druh`.

Schéma je v [src/Katalog.Data/Schema](../src/Katalog.Data/Schema). Všetky tabuľky sú STRICT, väzby strážia cudzie kľúče,
CHECK obmedzenia a spúšťače (súbor je na disku svojho priečinka, epizódy má len seriál, verzia patrí filmu alebo epizóde).
Kódy v databáze sú enumy z `Katalog.Jadro.Model.Kody`; test overuje, že sa zhodujú s CHECK obmedzeniami.

## Rozhodnutia A1–A10

| Č. | Rozhodnutie |
| --- | --- |
| A1 | Jedna knižnica (súbor SQLite) s viacerými katalógmi vnútri; katalóg sa dá exportovať a importovať samostatne |
| A2 | Titul existuje aj bez súborov |
| A3 | Verzia ako most medzi súbormi a titulom |
| A4 | Jeden dotazovací jazyk pre hľadanie, filtre, playlisty, náhodný výber, štítky a web |
| A5 | Pôvod pri každom údaji a zámky pri ručných úpravách |
| A6 | Zdroje a úložiská ako vstavané moduly s jednotným rozhraním |
| A7 | Trvalá fronta úloh s pruhmi podľa diskov |
| A8 | Tajomstvá mimo knižnice, šifrované ochranou údajov Windows |
| A9 | Migrácie schémy len dopredu, so zálohou pred zmenou, testované v GitHub Actions |
| A10 | Cesty ako strom priečinkov (rodič a meno) |

## Výkonové ciele

Merajú sa automaticky v GitHub Actions (úloha „Výkon“) na vymyslenej knižnici s 1 miliónom súborov.

| Úkon | Cieľ |
| --- | --- |
| Spustenie programu | do 2 s |
| Hľadanie počas písania | do 100 ms pri 1 milióne súborov |
| Prvé skenovanie interného SSD | 100 000 súborov do 1 min |
| Aktualizácia bez zmien, interný SSD | 100 000 súborov do 10 s |
| Plagátová stena | plynulé posúvanie pri 10 000 tituloch |
| Pamäť | do 300 MB pri 1 milióne súborov |

## Etapy

E0 základ · E1 disky, katalógy a skenovanie · E2 hľadanie a prehrávanie · **0.1.0** · E3 technické údaje videa ·
E4 knižnica, HISTRA a kolekcie · E5 duplicity, zdravie diskov, prehľady · E6 štítky, tlač a export ·
E7 playlisty a náhodný výber · E8 vzdialené úložiská · E9 webový katalóg · **1.0**
