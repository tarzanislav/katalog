# KATALOG – hľadanie

Pole hľadania hore v okne hľadá v názvoch súborov a priečinkov všetkých diskov knižnice, aj keď disk nie je
pripojený. Výsledky sa ukážu počas písania (po krátkej pauze), **Enter** hľadá hneď, **Esc** hľadanie zruší.
Rovnaký zápis používajú filtre, uložené hľadania, inteligentné a náhodné playlisty a neskôr aj webový katalóg.

## Slová

| Zápis | Čo nájde |
| --- | --- |
| `šťastný film` | názvy, ktoré obsahujú obe slová (v ľubovoľnom poradí, kdekoľvek v slove) |
| `stastny film` | to isté – diakritika ani veľkosť písmen nerozhodujú |
| `"šťastný film"` | presne túto frázu |
| `-ukážka` | názvy bez tohto slova |
| `s0` | aj jedno- a dvojpísmenové slová (pomalšie, porovnáva sa celý názov) |

## Polia

| Zápis | Čo nájde |
| --- | --- |
| `pripona:mkv,mp4` | súbory s jednou z prípon |
| `typ:video` | videá; ďalej `titulky`, `zvuk`, `obrazok`, `dokument`, `archiv`, `subor` (všetky súbory), `priecinok` |
| `velkost:>4GB` | väčšie ako 4 GB; aj `>=`, `<`, `<=`, `=`; jednotky B, kB, MB, GB, TB (po 1024 ako Windows) |
| `velkost:4GB` | aspoň 4 GB (holé číslo bez jednotky sú GB) |
| `velkost:1GB..8GB` | od 1 do 8 GB; aj `..8GB` alebo `1GB..` |
| `zmenene:2024` | zmenené v roku 2024; aj mesiac `2024-05`, deň `1.3.2025` alebo `2025-03-01`, `dnes`, `vcera` |
| `zmenene:>=2024-05` | od mája 2024; `>` po, `<` pred, `<=` do vrátane, `2023..2024` rozsah |
| `zmenene:30d` | za posledných 30 dní; `t` týždne, `m` mesiace, `r` roky; `<30d` staršie ako 30 dní |
| `pridane:7d` | prvýkrát naskenované za posledných 7 dní (čo pribudlo) |
| `disk:3` | na disku 003; aj `disk:3,7` alebo podľa názvu `disk:"Filmy 1"` |
| `katalog:Videotéka` | v katalógu, ktorého názov obsahuje text |
| `pripojene:ano` | len na diskoch pripojených teraz; `pripojene:nie` len na nepripojených |
| `zmazane:aj` | aj to, čo z diskov zmizlo (história); `zmazane:len` len to |

### Technické údaje videí

Prečíta ich KATALOG sám na pozadí po skene a po pripojení disku (ffprobe); kým nie sú prečítané, tieto polia video
nenájdu.

| Zápis | Čo nájde |
| --- | --- |
| `kvalita:4k` | podľa rozlíšenia: `sd`, `hd`, `fhd`, `4k` (aj `720p`, `1080p`, `2160p`); `kvalita:>=fhd` Full HD a lepšie |
| `kodek:hevc` | kodek obrazu: `hevc` (aj `h265`, `x265`), `h264` (`avc`), `av1`, `vp9`, `mpeg2`, `mpeg4` (`xvid`, `divx`), `vc1` |
| `zvuk:sk,cs` | so zvukovou stopou v jednom z jazykov (`sk`, `slo`, `cze`, `cs`… je jedno); `zvuk:nie` bez zvuku |
| `titulky:sk` | s titulkami v jazyku – vnútri videa aj ako súbor vedľa neho (`Film.sk.srt`); `titulky:ano`, `titulky:nie` |
| `hdr:ano` | s HDR; `hdr:dv` (Dolby Vision), `hdr:hdr10`, `hdr:hlg`, `hdr:nie` |
| `dlzka:>2h` | dlhšie ako 2 hodiny; `dlzka:<30min`, `dlzka:80..100` (holé číslo sú minúty), `dlzka:<=1:30` |
| `poskodene:ano` | súbory, ktoré ffprobe nevedel prečítať (poškodené, nedokončené) |

### Knižnica titulov

Platia v poli hľadania (nájdu súbory verzií – filmu, epizódy) aj vo filtri knižnice nad stenou plagátov (nájdu tituly).
Názvy, žánre a hercov doplnia zdroje údajov (TMDB, HISTRA); bez nich poznajú len to, čo povedia názvy súborov.

| Zápis | Čo nájde |
| --- | --- |
| `rok:2001` | rok titulu; `rok:1990..1999`, `rok:>=2020` |
| `zaner:komedia` | žáner (časť názvu, bez diakritiky); `zaner:komedia,horor` jeden z nich |
| `hodnotenie:>=7.5` | hodnotenie zdroja (0 – 10); holé číslo znamená aspoň |
| `moje:>=8` | tvoje vlastné hodnotenie (1 – 10) |
| `herec:"Meno Priezvisko"` | hrá v ňom (aspoň 3 písmená mena); `rezia:Priezvisko` réžia, pri seriáli tvorcovia |
| `kolekcia:ano` | patrí do kolekcie; `kolekcia:"Názov"` do kolekcie s týmto názvom; `kolekcia:nie` do žiadnej |
| `videne:nie` | nevidené (pri súbore jeho film alebo epizóda, pri seriáli žiadna epizóda) |
| `mam:nie` | tituly, ktoré nemáš: chýbajúce diely kolekcií, zoznamy HISTRA, želania |
| `zelane:ano` | v zozname želaní |
| `verzie:>1` | duplicity: titul máš viackrát (pri seriáli niektorú epizódu); `verzie:2` presne dvakrát |
| `zalohovane:nie` | tituly len na jednom disku – stratíš ich s ním (pri seriáli stačí jedna taká epizóda); `zalohovane:ano` na aspoň dvoch |
| `stitok:Vianoce` | tvoj štítok titulu (časť názvu, bez diakritiky); `stitok:ano` akýkoľvek, `stitok:nie` žiadny |
| `rozpozerane:ano` | seriály, ktoré si začal: niektorú epizódu si videl a nevidenú máš na disku |
| `pozicane:ano` | požičané teraz: titul sám alebo disk s jeho súborom (súbor: jeho disk alebo titul); `pozicane:Meno` komu, `pozicane:nie` nič z toho |
| `kniznica:film` | filmy; `kniznica:serial` seriály, `kniznica:potvrdit` čaká na potvrdenie, `kniznica:ano` súbor je verziou titulu, `kniznica:nie` video odložené mimo knižnice |

Súbory majú aj pole `duplikat:ano` – presná kópia toho istého súboru inde (rovnaká veľkosť a rovnaký začiatok aj koniec;
odtlačok sa zistí pri čítaní technických údajov videa).

Vo filtri knižnice platia aj podmienky súborov (`kvalita:4k`, `zvuk:cs`, `disk:3`…): titul ich spĺňa, keď ich má
niektorý súbor jeho verzie, ktorý je na disku.

Pred pole sa dá dať mínus: `-pripona:srt` sú súbory okrem titulkov, `-zaner:horor` tituly bez hororu. Všetky
podmienky musia platiť naraz. Čo program nerozumie, ohlási nad výsledkami a vynechá – nikdy nehádže potichu. Neznáme
pole (napr. `farba:modra`) hľadá ako obyčajný text.

## Príklady

```
typ:video velkost:>20GB                 veľké filmy (napr. kandidáti na iný disk)
typ:video pripojene:ano                 čo sa dá prehrať hneď
pridane:7d typ:video                    čo pribudlo tento týždeň
"S01E01" typ:video                      prvé diely seriálov
typ:titulky -pripona:srt                titulky v iných formátoch ako SRT
zmazane:len typ:video                   videá, ktoré z diskov zmizli (ostali v histórii)
kvalita:4k hdr:ano                      4K s HDR
typ:video -zvuk:sk -titulky:sk          videá bez slovenčiny (ani zvuk, ani titulky)
kvalita:<=hd typ:video                  kandidáti na lepšiu verziu
zaner:komedia videne:nie                nevidené komédie (vo filtri knižnice)
kolekcia:ano mam:nie                    chýbajúce diely kolekcií
kniznica:serial -zvuk:cs                seriály bez českého zvuku
zalohovane:nie hodnotenie:>=8           najlepšie tituly, ktoré ešte nemáš zálohované
stitok:vianoce videne:nie               vianočné tituly, ktoré si ešte nevidel
pozicane:jano                           čo má požičané Jano
rozpozerane:ano                         seriály, ktoré si začal pozerať (aj ako playlist)
```

Výsledkov je najviac 500; keď ich je viac, program to povie a stačí pridať slovo alebo filter.

## Prehrávanie

Dvojklik alebo Enter na videu ho prehrá, na priečinku ho ukáže v katalógu. Tlačidlo **Prehrať** prehrá všetko
vybrané naraz (ako playlist). Priečinok sa prehrá podľa obsahu: Blu-ray (BDMV) a DVD (VIDEO_TS) ako disk, inak
všetky videá priečinka (napr. celá sezóna). Keď disk nie je pripojený, KATALOG povie, ktorý disk pripojiť a kde
leží, a prehrávanie spustí sám, keď ho Windows rozpozná.

Prehrávač sa vyberá v **Nastaveniach**: predvolený prehrávač Windows alebo nájdený VLC, MPC-HC, MPC-BE, PotPlayer,
mpv či iný program. Priečinky Blu-ray a DVD predvolený prehrávač Windows zvyčajne nevie.
