# KATALOG

Súkromný katalóg videotéky a diskov pre Windows (pracovný názov). Raz naskenuje každý disk – USB, interný, NAS,
DVD či Blu-ray – a potom jeho obsah ukáže a prehľadá aj vtedy, keď disk nie je pripojený. Filmy a seriály zobrazuje
ako knižnicu s plagátmi a technickými údajmi, ostatné súbory ako prehľadný strom.

## Súkromie

- **Údaje používateľa nikdy neopustia jeho počítač.** Knižnica, názvy súborov a filmov, disky ani zoznamy sa nedostanú
  do tohto repozitára, do testov ani do hlásení chýb. Testy používajú len vymyslené názvy.
- Na internet ide len hľadaný názov titulu do zapnutých zdrojov (TMDB, HISTRA) a kontrola vydaní na GitHube.
  Žiadna telemetria.
- Kľúče a heslá ležia mimo knižnice, šifrované ochranou údajov Windows.

## Stavba

Jadro programu nezávisí od okien, databázy ani internetu; ostatné časti sa naň len pripájajú.

| Projekt | Úloha |
| --- | --- |
| `Katalog.Jadro` | Entity a kódy dátového modelu, pravidlá, rozhrania zdrojov a úložísk, aktualizácie |
| `Katalog.Data` | Knižnica SQLite: schéma, migrácie so zálohou, kontrola integrity, zapisovacia fronta, fulltextový index |
| `Katalog.Skenovanie` | Prechádzanie diskov, identita zväzkov, rozdielová aktualizácia |
| `Katalog.Media` | ffprobe a ffmpeg, rozpoznanie názvov, triedy kvality |
| `Katalog.Zdroje` | TMDB, HISTRA, Jellyfin a spájanie údajov |
| `Katalog.Ulohy` | Trvalá fronta úloh s pruhmi podľa diskov |
| `Katalog.Vystupy` | Webový katalóg, CSV, HTML, štítky a tlač, M3U |
| `Katalog.Okno` | Rozhranie WPF (program `Katalog.exe`) |
| `Katalog.Zdravie` | Malý pomocný program na čítanie zdravia diskov s právami správcu |
| `tests/Katalog.Testy` | Vlastný bežec testov, bez cudzieho testovacieho rámca |

Podrobný návrh je v [docs/architektura.md](docs/architektura.md), stav práce v [docs/stav-projektu.md](docs/stav-projektu.md).

## Zostavenie

Potrebný je .NET 10 SDK.

```
dotnet build Katalog.slnx -c Release
dotnet run --project tests/Katalog.Testy -c Release
```

Jadro, dáta a testy sa zostavia aj na Linuxe; okno WPF sa zostaví všade, ale spustí sa len vo Windows.
Inštalátor zostavuje GitHub Actions (Inno Setup). Vydanie vzniká len so súhlasom vlastníka.

## Licencia

Apache License 2.0, pozri [LICENSE](LICENSE). Program sa poskytuje bez akejkoľvek záruky.
Diela tretích strán sú uvedené v [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
