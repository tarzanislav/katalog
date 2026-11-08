# Diela tretích strán

KATALOG je vlastný kód (licencia Apache 2.0). Používa tieto diela tretích strán; ich licencie sa inštalujú s programom.

| Dielo | Na čo | Licencia |
| --- | --- | --- |
| [SQLite](https://sqlite.org) | databáza knižnice | voľné dielo (public domain) |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | prístup k SQLite z .NET | MIT |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) | natívna knižnica SQLite pre .NET (súčasť Microsoft.Data.Sqlite) | Apache 2.0 |
| [.NET](https://github.com/dotnet/runtime) | runtime programu | MIT |
| [FFmpeg](https://ffmpeg.org) 9.0.2 (priečinok `ffmpeg`) | technické údaje videí (ffprobe) a náhľady (ffmpeg) | GNU LGPL 2.1 alebo novšia |
| [zlib](https://zlib.net) (súčasť zostavy FFmpeg) | rozbaľovanie údajov niektorých formátov | zlib |
| [QR Code generator library](https://www.nayuki.io/page/qr-code-generator-library) (Project Nayuki) – postup kódovania, prepísaný v programe (`Katalog.Vystupy/QrKod.cs`), nie pribalený | QR kódy na štítkoch | MIT |

PDF, Excel (Office Open XML), CSV a HTML zapisuje KATALOG vlastným kódom, bez ďalších knižníc.

## Programy používateľa

- **WinSCP** (https://winscp.net, GNU GPL 3): ak je nainštalovaný, KATALOG ním číta zoznamy súborov na serveroch
  (SFTP, FTP, WebDAV, S3) podľa lokalít uložených vo WinSCP. Spúšťa ho ako samostatný program, nepribaľuje ho
  a heslá ani kľúče nikdy nevidí.

## Zdroje údajov

KATALOG sa môže pýtať služieb na údaje o filmoch a seriáloch – len ak ich používateľ zapne a dá im svoj vlastný kľúč;
posiela sa len hľadaný názov a rok titulu. Údaje a obrázky ostávajú majetkom služieb a ich autorov, KATALOG ich
uchováva len v počítači používateľa.

- **TMDB** (https://www.themoviedb.org): názvy, dej, žánre, osoby, plagáty, epizódy a kolekcie.
  *This product uses the TMDB API but is not endorsed or certified by TMDB.*
- **HISTRA** (https://histra.net): hľadanie titulov a zoznamy používateľa (len čítanie).
- **Jellyfin** (server používateľa): kolekcie a pozreté tituly.

## FFmpeg (LGPL 2.1 alebo novšia)

KATALOG prednostne použije FFmpeg z UNIVERSUM alebo z priečinka zvoleného v Nastaveniach; vlastná kópia v priečinku
`ffmpeg` je záloha pre počítač bez nich. Je to samostatný program (KATALOG ho len spúšťa), zostavený z oficiálneho
zdrojového kódu FFmpeg (značka `n9.0.2`, commit `946fcce07b6dcd0331c8cc609192aeff5e1924f8`) skriptami
`tools/ffmpeg/build-ffmpeg.sh` a `tools/ffmpeg/prepare.sh` – bez súčastí pod GPL, okresaný na čítanie formátov a výrobu
náhľadov JPEG. Text licencie (`COPYING.LGPLv2.1`) a popis zostavy (`SOURCE.txt`) sa inštalujú s ním.
**Úplný zdrojový kód tejto zostavy** (FFmpeg a skripty zostavenia) je priložený ku každému vydaniu KATALOGU na GitHube
ako `ffmpeg-9.0.2-source.tar.xz` (https://github.com/tarzanislav/katalog/releases).

## MIT (Microsoft.Data.Sqlite, .NET, QR kód)

Copyright (c) .NET Foundation and Contributors

Copyright (c) Project Nayuki (QR Code generator library, https://www.nayuki.io/page/qr-code-generator-library)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Apache 2.0 (SQLitePCLRaw)

Copyright 2014-2024 SourceGear, LLC. Licensed under the Apache License, Version 2.0 – its full text is the file
LICENSE of this program (the same licence).
