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
  Verejný kľúč vydaní zatiaľ nie je nastavený, preto program sám nič neinštaluje, len otvorí stránku vydania.
- Hlavné okno s tromi stĺpcami, svetlý a tmavý motív, okno O programe, ikona.
- Samotest (`Katalog.exe --samotest`) so snímkami okna; GitHub Actions: testy na Linuxe, výkonový test,
  zostavenie, samotest a inštalátor na Windows, vydanie s podpisom.
- Vlastný bežec testov (bez cudzieho rámca); testy používajú len vymyslené názvy.

Čaká na vlastníka (pred prvým vydaním 0.1.0):

- Vytvoriť kľúč vydaní (`tools/New-ReleaseKey.ps1`) a prostredie `release` s tajomstvom `KATALOG_RELEASE_KEY`
  a schvaľovaním (Required reviewers).
- Definitívny názov programu (pracovný názov KATALOG je v kóde na jednom mieste: `Katalog.Jadro/Produkt.cs`
  a `Directory.Build.props`).

Ďalšia etapa: **E1 – disky, katalógy a skenovanie.**
