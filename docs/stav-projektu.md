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

## Etapa E2 – hľadanie a prehrávanie (8. 11. 2026, vetva vyvoj/etapa2)

Hotové:

- **Jeden dotazovací jazyk** (rozhodnutie A4) v jadre: slová, frázy, záporné slová a polia `pripona`, `typ`,
  `velkost`, `zmenene`, `pridane`, `disk`, `katalog`, `pripojene`, `zmazane`. Čo nerozumie, ohlási a vynechá.
  Návod je v [hladanie.md](hladanie.md).
- **Hľadanie počas písania** v názvoch súborov a priečinkov všetkých diskov, aj nepripojených; slová idú cez
  fulltextový index, polia ako podmienky s parametrami (nič z textu sa nestane príkazom SQL).
- **Filtre** nie sú druhý spôsob hľadania: zapisujú polia do textu v poli hľadania.
- **Uložené hľadania** v Zbierkach (spustiť, premenovať, zmazať).
- **Prehrávanie**: predvolený prehrávač Windows alebo VLC, MPC-HC, MPC-BE, PotPlayer, mpv či iný program
  (Nastavenia); viac súborov ako playlist, celý priečinok (napr. sezóna), priečinky Blu-ray a DVD. Keď disk nie je
  pripojený, okno povie, ktorý pripojiť, a prehrávanie spustí samo po jeho pripojení.
- **Výkon** pri 1 milióne súborov (GitHub Actions): samotné slová do 5 ms, slová s poľami okolo 100 ms, len polia
  do 80 ms.
- Okno Nastavenia (prehrávač, motív, aktualizácie); poznámky k vydaniu 0.1.0 pripravené.
- Samotest: hľadanie z okna, prehratie súboru aj sezóny (bez spustenia prehrávača), filtre, uložené hľadania, snímky.

## Etapa E3 – technické údaje videa (8. 11. 2026, vetva vyvoj/etapa3)

Hotové:

- **Čítanie technických údajov** každého videa cez ffprobe: kontajner, dĺžka, dátový tok, kodek a rozlíšenie obrazu,
  trieda kvality (SD, HD, FHD, 4K – rovnaké hranice ako UNIVERSUM), HDR (HDR10, HLG, Dolby Vision), snímky za sekundu,
  zvukové stopy a titulky s jazykmi, kanálmi a príznakmi (predvolené, vynútené, pre nepočujúcich). Aj **externé
  titulky** vedľa videa (`Film.sk.forced.srt`). Poškodené súbory sa zapíšu s chybou (N3), program nespadne.
- **Náhľad** videa (ffmpeg, tretina dĺžky, 320 px, asi 15 kB) v priečinku `<knižnica>.obrazky` vedľa knižnice – podľa R10
  len pre disky, pri ktorých ho používateľ zapne (predvolene vypnuté; opravené na začiatku E4).
- Číta sa **na pozadí s nízkou prioritou** po skene a po pripojení disku, jeden súbor naraz na disk; zastaviť sa dá
  v stavovom riadku, spustiť z ponuky disku. Vypnúť sa dá v Nastaveniach.
- **Trvalá fronta úloh** (A7): sken aj čítanie sú úlohy v knižnici; sken prerušený zatvorením programu pokračuje po
  ďalšom pripojení disku, čítanie pokračuje tam, kde skončilo (presunuté z E1).
- **FFmpeg** (vlastník 8. 11. 2026): prednostne z UNIVERSUM alebo z priečinka v Nastaveniach, inak **vlastný FFmpeg
  9.0.2 v inštalátore** – LGPL, zostavený zo zdrojového kódu, okresaný (ffprobe a ffmpeg po 21 MB), spúšťa sa len keď
  sedí manifest SHA-256. Zdrojový kód sa prikladá ku každému vydaniu, licencia v THIRD-PARTY-NOTICES.
- **Hľadanie** podľa technických údajov: `kvalita`, `kodek`, `zvuk`, `titulky`, `hdr`, `dlzka`, `poskodene`;
  filtre Kvalita, Jazyk zvuku, Jazyk titulkov.
- Detail videa: obraz, dĺžka, zvuk a titulky s jazykmi, chyby čítania a náhľad.
- Testy čítajú skutočné vymyslené video, ktoré urobí ffmpeg (Linux aj Windows); samotest používa FFmpeg nainštalovaný
  s KATALOGOM.

## Etapa E4 – knižnica, HISTRA a kolekcie (8. 11. 2026, vetva vyvoj/etapa4)

Hotové:

- **Rozpoznanie filmov a seriálov z názvov** súborov a priečinkov, bez internetu a aj z nepripojených diskov: knižnica
  UNIVERSUM (`MOVIES`, `TVSHOWS\…\Season 01`, `[tmdbid-…]`) presne, ostatné názvy (release mená, `S01E04`, `1x04`,
  `Díl 4`, priečinky sérií, špeciály) ako návrh. CD1 a CD2 sú jedna verzia, BDMV a VIDEO_TS tiež; ukážky a extra sa
  odložia. Rovnaký titul pod rôznymi menami (s diakritikou aj bez, slovenský aj originálny názov) je jeden titul.
- **Dátový model knižnice** (schéma v4): verzie na diskoch, epizódy aj viacepizódne súbory, kandidáti zdrojov,
  súbory mimo knižnice, kľúče názvov. Pôvod každého poľa a zámky (A5): tvoja úprava sa nikdy neprepíše, dá sa vrátiť
  zdrojom; zlúčenie dvoch záznamov toho istého titulu so všetkým, čo k nim patrí.
- **Zdroje údajov** s jedným rozhraním (A6): **TMDB** (slovenské, inak české názvy a dej, žánre, osoby, plagáty,
  všetky epizódy, kolekcie), **HISTRA** (hľadanie titulov a tvoje zoznamy – len čítanie) a **Jellyfin** (kolekcie a
  pozreté, nepovinné). Posiela sa len hľadaný názov a rok. Titul sa vezme sám len pri jednoznačnom názve s rokom (R5),
  inak čaká **Na potvrdenie** s návrhmi vedľa seba.
- **Kľúče** (vlastník 8. 11. 2026): prevezmú sa z UNIVERSUM (Nastavenia píšu „Momentálne prepojené na … z UNIVERSUM“),
  vlastný kľúč má prednosť; bez kľúčov v UNIVERSUM sú len polia na nové. Ukladajú sa mimo knižnice, šifrované
  ochranou údajov Windows (A8).
- **Plagáty a údaje sa ukladajú lokálne** (R7) – knižnica funguje offline; plagát sa uloží raz podľa obsahu.
- **Okno knižnice**: stena plagátov (riadky sa kreslia len viditeľné) aj zoznam – Filmy, Seriály, Na potvrdenie,
  Zoznam želaní, Kolekcie (aj diely, ktoré nemáš), Zoznamy HISTRA (mám / nemám). Detail titulu: plagát, dej, žánre,
  réžia, herci, verzie na diskoch, série s chýbajúcimi epizódami, kolekcie; prehranie (seriál ako playlist),
  videné, želané, moje hodnotenie, poznámka, ručná úprava, „Hľadať inak…“, „Nie je to film ani seriál“.
- **Dotazovací jazyk** (A4): `rok`, `zaner`, `hodnotenie`, `moje`, `herec`, `rezia`, `kolekcia`, `videne`, `mam`,
  `zelane`, `kniznica` – v hľadaní súborov aj vo filtri knižnice (tam platia aj podmienky súborov ako `kvalita:4k`).
- O programe: upozornenie TMDB; zdroje v THIRD-PARTY-NOTICES.
- **Výkon** (GitHub Actions): 10 000 titulov – stena filmov okolo 40 ms, filter okolo 50 ms, rozpoznanie 14 000 videí
  pár sekúnd (merané aj lokálne).
- Samotest: vymyslený zdroj namiesto TMDB (nič nejde na internet), plagáty nakreslené samotestom, potvrdenie návrhu,
  kolekcia s chýbajúcim dielom, prehranie seriálu, snímky steny, zoznamu, seriálu, Na potvrdenie, kolekcií a Nastavení.

Otvorené:

- **Logo TMDB** v okne O programe: podmienky TMDB ho chcú pri upozornení; oficiálny súbor loga sa tu nedal stiahnuť –
  doplní sa, keď ho vlastník priloží (zatiaľ je tam text upozornenia).
- Fotky hercov a pozadia (prídu s webovým katalógom E9, plagáty v troch veľkostiach tiež).
- Definitívny názov programu (pracovný názov KATALOG je v kóde na jednom mieste: `Katalog.Jadro/Produkt.cs`
  a `Directory.Build.props`).

**Vydania** (so súhlasom vlastníka, podpis zoznamu súčtov a súčet inštalátora overené): **0.1.0** a **0.2.0**
8. 11. 2026. Poznámky k vydaniu 0.3.0 (E4) sú pripravené; vydá sa len so súhlasom vlastníka.

Ďalšia etapa: **E5 – duplicity, zdravie diskov, prehľady**: duplicitné tituly a súbory, tituly len na jednom disku
(N1), zdravie diskov (SMART), prehľad diskov a knižnice.
