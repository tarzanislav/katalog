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
- **Výkon** (GitHub Actions): 10 000 titulov – stena filmov 58 ms, filter 49 ms, rozpoznanie 14 000 videí 2,4 s.
- Samotest: vymyslený zdroj namiesto TMDB (nič nejde na internet), plagáty nakreslené samotestom, potvrdenie návrhu,
  kolekcia s chýbajúcim dielom, prehranie seriálu, snímky steny, zoznamu, seriálu, Na potvrdenie, kolekcií a Nastavení.

## Etapa E5 – duplicity, zdravie diskov, prehľady (8. 11. 2026, vetva vyvoj/etapa5)

Hotové:

- **Duplicity**: titul viackrát je jeden titul s viacerými verziami; pohľad Duplicity, pole `verzie:>1`. Najlepšia verzia
  sa označí v detaile aj s dôvodom; poradie kritérií (rozlíšenie, CZ/SK zvuk, titulky, kodek, veľkosť, HDR) sa dá
  zmeniť v Nastaveniach.
- **Presné kópie súborov**: odtlačok (veľkosť, prvý a posledný megabajt) sa zistí pri čítaní technických údajov videa,
  aj pre videá prečítané skôr; pole `duplikat:ano`.
- **Nezálohované tituly (N1)**: pohľad Len na jednom disku, pole `zalohovane:nie` (film na jednom disku, seriál s epizódou
  na jednom disku). Názov bez roka (napr. záloha seriálu) sa priradí k jedinému titulu toho mena s rokom.
- **Prehľad diskov**: obsadenosť, súbory, tituly, „Len tu“ (čo by sa stratilo s diskom), posledný sken, pripojenie,
  zdravie a jeho história.
- **Kam sa to zmestí (N2)**: k zadanej veľkosti navrhne disky s miestom, najtesnejšie prvé.
- **Zdravie diskov**: malý pomocník Katalog.Zdravie sa spustí len na požiadanie s právami správcu (Windows sa opýta),
  prečíta SMART diskov ATA/SATA a záznam zdravia NVMe (USB, ak to rámček dovolí), zapíše len čísla do nového súboru;
  hlavný program práva správcu nemá. Pravidlá čítania sú v jadre a otestované; disky sa priradia podľa sériového čísla.
  Varovanie: presunuté, čakajúce či neopraviteľné sektory, chyby média, teplota, opotrebenie SSD; zlé: atribút pod
  prahom alebo disk hlási zníženú spoľahlivosť.
- **Štatistiky (N5)**: filmy, seriály, epizódy, hodiny sledovania, kvalita, desaťročia, žánre, tituly na diskoch.
- Samotest: duplicita a najlepšia verzia, nezálohované, prehľad so zdravím (vymyslená odpoveď pomocníka), štatistiky.

## Etapa E6 – výstupy (8. 11. 2026, vetva vyvoj/etapa6)

Hotové (všetko vlastným kódom, bez nových knižníc – rozhodnutie vlastníka 8. 11. 2026):

- **QR kódy** (ISO/IEC 18004, všetky verzie a úrovne opravy, UTF-8): zhodné bit po bite s nezávislým kódovačom,
  overené aj čítačkami QR kódov.
- **Štítky diskov** (číslo D007, názov, katalóg, kapacita, súbory, tituly, kde disk leží) a **štítky titulov** na obaly
  (disky, na ktorých titul leží, názov, rok, kvalita) s QR kódom. Formáty z EviTA: hárky A4 (24, 21, 40, 65 a 14 štítkov)
  aj tlačiareň štítkov; prvý voľný štítok načatého hárka, kópie, rámček na strihanie, posun tlače. Náhľad strán,
  tlač a PDF.
- **Export každého zoznamu** (výsledky hľadania, obsah priečinka, tituly knižnice s diskami, prehľad diskov, požičané)
  do **Excelu** (čísla ako čísla, veľkosti v GB, dátumy ako dátumy, filter a ukotvený nadpis; text sa nikdy nestane
  vzorcom), **CSV** pre slovenský Excel, **HTML** (jedna stránka, triedenie kliknutím, svetlá aj tmavá) a **PDF**;
  tlač zoznamov s nadpisom na každej strane a číslami strán.
- **PDF** zapisuje program sám: strany ako obrázky bez straty (Flate), sivé strany v odtieňoch sivej – slovenské
  písmená vyzerajú presne ako na obrazovke, bez vkladania písma.
- **Export a import katalógu (A1)**: katalóg do samostatného súboru knižnice (disky, priečinky, súbory, technické
  údaje, skeny, zdravie, tituly na nich s osobami, žánrami, kolekciami, štítkami a pozretiami; obrázky nie – zdroje ich
  načítajú znova). Import z ľubovoľného súboru knižnice (aj staršej schémy): disky, ktoré knižnica už má, vynechá,
  obsadené čísla diskov prečísluje, známe tituly (podľa identifikátora zdroja alebo názvu a roka) spojí. Celý import je
  jeden zápis (všetko alebo nič), pred ním záloha; súbor z cudzieho zdroja sa číta len vo vlastnej kópii a do knižnice
  idú len jej vlastné stĺpce.
- **Výpožičky (N6)**: požičať disk (strom diskov) alebo titul (detail) – komu, poznámka; zoznam Požičané s históriou,
  vrátenie, export; pole `pozicane:ano` / `pozicane:Meno` (titul požičaný sám alebo s diskom).
- **Štítky titulov** (vlastné značky, napr. Vianoce): v detaile titulu, pohľad Štítky v Zbierkach, pole `stitok:`.
- Schéma knižnice **v5**: kľúče štítkov a mien bez diakritiky, najviac jedna otvorená výpožička disku či titulu.
- Samotest: štítky v okne aj v PDF, export výsledkov do všetkých formátov, tabuľka titulov s diskami, prehľad,
  výpožička a štítok s ich poliami, export katalógu a import späť (disk už v knižnici je).

## Etapa E7 – playlisty a náhodný výber (8. 11. 2026, vetva vyvoj/etapa7)

Hotové:

- **Playlisty** troch druhov: **ručný** (tituly v tvojom poradí: pridať z detailu titulu, posunúť, odobrať),
  **inteligentný** (dotaz jedného jazyka A4, chronologicky, dopĺňa sa sám) a **náhodný** (daný počet titulov vylosovaný
  z dotazu; žreb ostáva rovnaký, kým nedáš Vylosovať znova). Losovanie je vlastný generátor s pevnou postupnosťou
  (rovnaký výsledok na každom počítači), overený nezávislým skriptom.
- **Návrhy playlistov** z knižnice: nevidené filmy, to najlepšie nevidené, rozpozerané seriály, na krátky večer,
  tvoje obľúbené, najväčšie žánre, tvoje štítky, maratóny kolekcií (len s aspoň dvoma titulmi); jedným klikom sa
  uložia ako playlist.
- **Prehratie playlistu** celého alebo zamiešaného: film hrá najlepšiu verziu (na pripojenom disku, ak je), **seriál
  ďalšiu nevidenú epizódu**; tituly na nepripojených diskoch sa vynechajú a okno povie, ktoré disky chýbajú.
- **Export do M3U** (.m3u8) pre iné prehrávače – cesty súborov na pripojených diskoch.
- **Vyber mi niečo**: tlačidlo Náhodne vyberie titul spomedzi zobrazených, ktoré máš (platí filter, napr.
  `videne:nie zaner:komedia`); ďalší klik vyberie iný, kým sa nevystriedajú všetky.
- Pole dotazu `rozpozerane:ano`; schéma knižnice **v6** (počet a semienko náhodného playlistu).
- Samotest: playlisty a návrhy na stene, ručný playlist v poradí, prehratie playlistu (epizóda, potom film), M3U,
  náhodný playlist, dva rôzne náhodné výbery za sebou, otvorenie návrhu.

## Etapa E8 – vzdialené úložiská (8. 11. 2026, vetva vyvoj/etapa8)

Hotové (rozhodnutie vlastníka 8. 11. 2026: servery cez nainštalovaný WinSCP, bez novej knižnice):

- **Server ako disk katalógu**: Pridať disk → Server cez WinSCP – lokalita uložená vo WinSCP (SFTP, FTP, WebDAV, S3)
  a priečinok na serveri. KATALOG pozná len meno lokality; heslo či kľúč ostáva vo WinSCP. Lokality sa čítajú z
  registra používateľa alebo z WinSCP.ini prenosného WinSCP (len názvy). WinSCP.com sa nájde sám (inštalácia, PATH),
  inak ho používateľ vyberie.
- **Čítanie stromu**: príkazy idú WinSCP.com ako argumenty (Unicode – slovenské názvy prejdú) a výsledok v jeho XML
  zázname; strom sa číta po úrovniach, veľa priečinkov jedným spojením (pod limitom príkazového riadka), odkazy sa
  preskočia (nezacyklí sa). Nečitateľný priečinok sa ohlási a jeho obsah ostane. Zrušenie skenu ukončí aj WinSCP.
- Pred pridaním sa pripojenie overí jedným spojením so zrozumiteľnou chybou (napr. treba uložiť heslo a raz sa
  pripojiť vo WinSCP, aby poznal kľúč servera).
- Disk servera sa aktualizuje na požiadanie (nie je „pripojený“ ako USB disk); súbory sa dajú hľadať a rozpoznať do
  knižnice; prehrávanie zo servera program vysvetlí (KATALOG cez WinSCP len katalogizuje).
- **Sieťové disky a NAS** (`\\server\zdielanie`) fungujú ako doteraz (od E1), s časovým limitom, aby nedostupný server
  nezdržal okno.
- Testy proti vymyslenému serveru, ktorý odpovedá ako záznam WinSCP (úrovne, dávky, chyby, úvodzovky, lokality z
  WinSCP.ini); samotest pridá, naskenuje a rozpozná disk vymysleného servera.

## Etapa E9 – webový katalóg (8. 11. 2026, vetva vyvoj/etapa9)

Hotové (rozhodnutie vlastníka 8. 11. 2026: stránky do priečinka, s plagátmi):

- **Súkromný webový katalóg**: Export a tlač → Webový katalóg… vytvorí do priečinka statické stránky – otvoria sa priamo
  z disku v prehliadači (aj v mobile), alebo ich vlastník nahrá, kam chce. Žiadna adresa na internete, žiadna knižnica
  z cudzieho servera.
- **Všetko zašifrované heslom**: AES-256-GCM, kľúč z hesla cez PBKDF2-SHA-256 (600 000 kôl) – to isté počíta prehliadač
  vlastným Web Crypto. Bez hesla sú v priečinku len náhodné bajty; heslo sa nikam neukladá. Údaje a plagáty sú skripty
  (data.js, plagaty/NNN.js), lebo stránka otvorená z disku smie načítať skript, nie súbor.
- **Stránka**: filmy, seriály, zoznam želaní, disky; hľadanie bez diakritiky v názvoch, hercoch, réžii, žánroch,
  štítkoch, kolekciách aj diskoch; žáner, poradie, len nevidené, náhodný výber; detail s dejom, ľuďmi a tým, kde titul
  leží (disk, priečinok, veľkosť, kvalita); disky s obsadenosťou a miestom, kde ležia. Svetlý aj tmavý vzhľad.
- **Plagáty** zmenšené na 240 bodov, po 40 v balíku; stránka načíta len balíky dlaždíc, ktoré ukazuje.
- Prepísať sa dá len prázdny priečinok alebo ten, kam sa katalóg uložil predtým (značka katalog-web.txt).
- Testy: šifrovanie (aj vektor PBKDF2 z RFC 7914), zápis priečinka, balíky, zlé heslo, zmenený obsah; stránka overená
  v prehliadači Chromium (heslo, plagáty, hľadanie, detail, disky, mobil). Samotest vytvorí web a odšifruje ho.

Otvorené:

- **Logo TMDB** v okne O programe: podmienky TMDB ho chcú pri upozornení; oficiálny súbor loga sa tu nedal stiahnuť –
  doplní sa, keď ho vlastník priloží (zatiaľ je tam text upozornenia).
- Fotky hercov a pozadia (prídu s webovým katalógom E9, plagáty v troch veľkostiach tiež).
- Definitívny názov programu (pracovný názov KATALOG je v kóde na jednom mieste: `Katalog.Jadro/Produkt.cs`
  a `Directory.Build.props`).
- Tlač na skutočnej tlačiarni, zdravie skutočných diskov a skutočný server cez WinSCP sa dajú vyskúšať len na počítači
  vlastníka.

**Vydania** (so súhlasom vlastníka – schválenie v Review deployments; podpis zoznamu súčtov a súčet inštalátora
overené): **0.1.0**, **0.2.0**, **0.4.0**, **0.5.0** a **0.6.0** 8. 11. 2026.

Všetky etapy E0 – E9 sú hotové. **1.0.0** vyjde po skúške vlastníka na jeho počítači (skutočné disky, tlačiareň, server cez WinSCP) – rozhodnutie vlastníka 8. 11. 2026; dovtedy 0.8.0.
