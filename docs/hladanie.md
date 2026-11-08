# KATALOG – hľadanie

Pole hľadania hore v okne hľadá v názvoch súborov a priečinkov všetkých diskov knižnice, aj keď disk nie je
pripojený. Výsledky sa ukážu počas písania (po krátkej pauze), **Enter** hľadá hneď, **Esc** hľadanie zruší.
Rovnaký zápis používajú filtre, uložené hľadania a neskôr aj playlisty, štítky a webový katalóg.

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

Pred pole sa dá dať mínus: `-pripona:srt` sú súbory okrem titulkov. Všetky podmienky musia platiť naraz.
Čo program nerozumie, ohlási nad výsledkami a vynechá – nikdy nehádže potichu. Neznáme pole (napr. `zvuk:cs`
pred etapou E3) hľadá ako obyčajný text.

## Príklady

```
typ:video velkost:>20GB                 veľké filmy (napr. kandidáti na iný disk)
typ:video pripojene:ano                 čo sa dá prehrať hneď
pridane:7d typ:video                    čo pribudlo tento týždeň
"S01E01" typ:video                      prvé diely seriálov
typ:titulky -pripona:srt                titulky v iných formátoch ako SRT
zmazane:len typ:video                   videá, ktoré z diskov zmizli (ostali v histórii)
```

Výsledkov je najviac 500; keď ich je viac, program to povie a stačí pridať slovo alebo filter.

## Prehrávanie

Dvojklik alebo Enter na videu ho prehrá, na priečinku ho ukáže v katalógu. Tlačidlo **Prehrať** prehrá všetko
vybrané naraz (ako playlist). Priečinok sa prehrá podľa obsahu: Blu-ray (BDMV) a DVD (VIDEO_TS) ako disk, inak
všetky videá priečinka (napr. celá sezóna). Keď disk nie je pripojený, KATALOG povie, ktorý disk pripojiť a kde
leží, a prehrávanie spustí sám, keď ho Windows rozpozná.

Prehrávač sa vyberá v **Nastaveniach**: predvolený prehrávač Windows alebo nájdený VLC, MPC-HC, MPC-BE, PotPlayer,
mpv či iný program. Priečinky Blu-ray a DVD predvolený prehrávač Windows zvyčajne nevie.
