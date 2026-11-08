# KATALOG – stav projektu

Aktualizuje sa po každej väčšej zmene.

## Etapa E0 – základ (8. 11. 2026, vetva vyvoj/etapa0)

Hotové:

- Riešenie `Katalog.slnx` s 10 projektmi; závislosti idú len smerom k jadru.
- **Schéma knižnice v1** so všetkými tabuľkami dátového modelu (katalógy až zdravie diskov), väzbami, CHECK
  obmedzeniami, spúšťačmi a fulltextovým indexom.
- **Výkon** (GitHub Actions, vymyslená knižnica s 1 miliónom súborov): hľadanie najviac 2,3 ms, knižnica 241 MB,
  zápis prvých 100 000 súborov 12 s. Zápis sa s rastúcou knižnicou spomaľuje (ďalších 900 000 za 346 s) –
  zrýchlenie hromadného zápisu je úloha etapy E1.
- Otvorenie knižnice: migrácie len dopredu, **číslovaná záloha** pred migráciou (posledných 10), rýchla kontrola
  integrity, odmietnutie cudzieho súboru a knižnice novšieho programu.
- **Jeden zapisovateľ** s dávkami a bodmi návratu: chybný zápis sa vráti sám, ostatné v dávke ostanú.
- Jadro: kódy dátového modelu, triedy kvality (zhodné s UNIVERSUM), rozhrania zdrojov údajov a úložísk,
  rozpoznanie disku podľa zväzku (body zhody), poradie zdrojov a zámky polí.
- **Aktualizácie**: kontrola vydaní raz denne, overenie podpisu vydania (ECDSA P-256) a súčtu inštalátora.
  Kľúč vydaní vytvoril vlastník 8. 11. 2026 na svojom počítači (`tools/Nastav-Vydania.ps1`); verejná časť je v programe,
  súkromná len ako tajomstvo `KATALOG_RELEASE_KEY` prostredia `release` so schvaľovaním.
- Hlavné okno s tromi stĺpcami, svetlý a tmavý motív, okno O programe, ikona.
- Samotest (`Katalog.exe --samotest`) so snímkami okna; GitHub Actions: testy na Linuxe, výkonový test,
  zostavenie, samotest a inštalátor na Windows, vydanie s podpisom.
- Vlastný bežec testov (bez cudzieho rámca); testy používajú len vymyslené názvy.

## Etapa E1 – disky, katalógy a skenovanie (8. 11. 2026, vetva vyvoj/etapa1)

Hotové:

- **Viac katalógov** v jednej knižnici: vytvoriť, premenovať, zmazať (len prázdny). Disky majú trvalé čísla
  (D001, D002, …) na štítky.
- **Pridanie disku**: celý pripojený disk, jeden jeho priečinok alebo sieťový disk / NAS podľa adresy. Úprava názvu,
  čísla a katalógu; odstránenie disku najprv urobí číslovanú zálohu knižnice.
- **Rozpoznanie disku podľa zväzku**, nikdy podľa písmena: GUID zväzku, sériové číslo, sériové číslo fyzického disku,
  zbernica (USB), sieťová adresa. Keď sa disk pripojí, okno to samo zistí a ponúkne aktualizáciu; pri neistej
  zhode sa opýta.
- **Skenovanie** na vlastnom vlákne s nízkou prioritou (disk ani počítač nebrzdí), porovnanie po priečinkoch:
  zapisuje sa len rozdiel. Zmazané súbory a priečinky sa len označia (dajú sa ukázať a pri návrate sa obnovia aj
  s údajmi), nečitateľné priečinky sa spočítajú a sken pokračuje, sken sa dá kedykoľvek zrušiť.
- **Výkon** (GitHub Actions): sken 100 000 súborov 4,6 s, opakovaný sken bez zmien 0,4 s. Knižnica s 1 miliónom
  súborov: prvých 100 000 za 7,3 s, ďalších 900 000 za 78 s (v E0 346 s), hľadanie najviac 1,9 ms, 226 MB.
- Schéma knižnice **v2** (koreň disku, zrušený nepotrebný index); migrácia z v1 so zálohou je otestovaná.
- Okno: strom katalógov, diskov a priečinkov, zoznam súborov, detail súboru alebo disku, priebeh skenu so zrušením,
  oznam o pripojenom disku, okno Pridať disk.
- Samotest pridá a naskenuje vymyslený disk, overí rozpoznanie, opakovaný sken, zmazanie súboru a zálohu.

Presunuté:

- Export a import jedného katalógu do samostatného súboru → E6 (výstupy).
- Trvalý zoznam úloh, ktorý po reštarte programu pokračuje v nedokončenom skene → E3 (fronta úloh pre technické
  údaje videa).

Otvorené:

- Definitívny názov programu (pracovný názov KATALOG je v kóde na jednom mieste: `Katalog.Jadro/Produkt.cs`
  a `Directory.Build.props`).

Ďalšia etapa: **E2 – hľadanie a prehrávanie** (po nej prvá verzia 0.1.0).
