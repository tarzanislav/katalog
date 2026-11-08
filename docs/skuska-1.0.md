# Skúška pred vydaním 1.0

Čo sa dá overiť len na počítači vlastníka (GitHub Actions nemá skutočné disky, tlačiareň ani server). Stačí prejsť
body, ktoré sa ťa týkajú; čo nefunguje, napíš aj so znením hlásenia (snímky obrazovky netreba ukladať nikam).

## Disky a skenovanie
- [ ] Pridať USB disk, interný disk a (ak máš) DVD/Blu-ray; po odpojení a pripojení USB disku ho program sám rozpozná
      (aj pod iným písmenom).
- [ ] Sieťový disk alebo NAS podľa adresy `\\server\zdielanie`; pri vypnutom NAS okno nezamrzne.
- [ ] Veľký disk (desaťtisíce súborov): sken beží na pozadí a dá sa zastaviť; opakovaný sken bez zmien je rýchly.

## Videá a knižnica
- [ ] Technické údaje videí (rozlíšenie, zvuk, titulky) a náhľady; FFmpeg z UNIVERSUM alebo pribalený.
- [ ] TMDB a HISTRA doplnia názvy, dej a plagáty; návrhy „Na potvrdenie“ sa dajú vybrať.
- [ ] Prehratie filmu, seriálu (ďalšia epizóda) a playlistu vo zvolenom prehrávači; Blu-ray a DVD priečinky.

## Zdravie diskov
- [ ] Prehľad diskov → Zistiť zdravie diskov: Windows sa opýta na povolenie; interné a USB disky ukážu teplotu,
      hodiny a stav (USB podľa rámčeka).

## Štítky a tlač
- [ ] Štítky diskov na hárku A4 (napr. 24 štítkov 70 × 37 mm): trafia do štítkov; inak upraviť Posun tlače.
- [ ] Začať od štítku na načatom hárku; tlačiareň štítkov (ak máš).
- [ ] QR kód zo štítka načítaný mobilom ukáže číslo a názov disku s diakritikou.
- [ ] Export zoznamu do Excelu, CSV (otvorí sa v slovenskom Exceli správne), HTML a PDF; tlač zoznamu.

## Servery cez WinSCP
- [ ] V Pridať disk sa ukážu lokality uložené vo WinSCP; server so uloženým heslom alebo kľúčom sa pridá a naskenuje.
- [ ] Lokalita bez uloženého hesla: zrozumiteľná chyba, nič nezamrzne.

## Webový katalóg
- [ ] Vytvoriť web s heslom, otvoriť index.html v prehliadači (Edge/Chrome/Firefox) aj v mobile; plagáty, hľadanie,
      detail s diskom a priečinkom.

## Ostatné
- [ ] Export katalógu do súboru a jeho import do inej knižnice (napr. na druhom počítači).
- [ ] Aktualizácia programu z 0.8.0 na ďalšie vydanie cez tlačidlo Aktualizácie.
- [ ] Svetlý a tmavý motív, okno pri malom rozlíšení.
