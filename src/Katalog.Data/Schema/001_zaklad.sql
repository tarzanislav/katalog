-- KATALOG library, schema version 1 (stage E0, 8 Nov 2026).
--
-- One library holds several catalogues (decision A1). Storages on the left: catalogue > disk > folder > file > media
-- data and tracks. Knowledge on the right: title > episode, collections, people, genres, tags. A version joins the two
-- worlds (A3): one or more files of one film or episode. A title without a version is wanted or a missing part (A2).
--
-- Rules of this file:
--  * every table is STRICT; times are UTC ticks (INTEGER, see Katalog.Jadro.Model.Cas);
--  * codes are the enums of Katalog.Jadro.Model.Kody; every CHECK lists exactly their values (a test keeps them in step);
--  * folder paths are a tree of parent and name (A10); a file also keeps its disk for fast queries – triggers make sure it
--    is the disk of its folder;
--  * nothing a migration has shipped is ever edited: changes come as new files 002_..., 003_...

CREATE TABLE nastavenie (
  kluc TEXT PRIMARY KEY,
  hodnota TEXT NOT NULL
) STRICT, WITHOUT ROWID;

CREATE TABLE katalog (
  id INTEGER PRIMARY KEY,
  nazov TEXT NOT NULL UNIQUE COLLATE NOCASE CHECK (length(trim(nazov)) > 0),
  farba TEXT,
  poradie INTEGER NOT NULL DEFAULT 0,
  vytvoreny INTEGER NOT NULL
) STRICT;

-- pictures are stored once by their content (SHA-256) in the cache folder next to the library
CREATE TABLE obrazok (
  id INTEGER PRIMARY KEY,
  odtlacok BLOB NOT NULL UNIQUE CHECK (length(odtlacok) = 32),
  druh INTEGER NOT NULL CHECK (druh BETWEEN 1 AND 4),
  sirka INTEGER NOT NULL CHECK (sirka > 0),
  vyska INTEGER NOT NULL CHECK (vyska > 0),
  velkost INTEGER NOT NULL CHECK (velkost >= 0),
  pridany INTEGER NOT NULL
) STRICT;

-- ---------------------------------------------------------------- storages

CREATE TABLE disk (
  id INTEGER PRIMARY KEY,
  katalog_id INTEGER NOT NULL REFERENCES katalog(id) ON DELETE CASCADE,
  -- the number printed on the label ("disk 003"); unique across the library
  cislo INTEGER NOT NULL UNIQUE CHECK (cislo > 0),
  nazov TEXT NOT NULL,
  druh INTEGER NOT NULL DEFAULT 0 CHECK (druh BETWEEN 0 AND 5),
  seriove_cislo_zvazku TEXT,
  guid_zvazku TEXT,
  seriove_cislo_disku TEXT,
  menovka TEXT,
  suborovy_system TEXT,
  velkost INTEGER CHECK (velkost IS NULL OR velkost >= 0),
  volne INTEGER CHECK (volne IS NULL OR volne >= 0),
  -- network share (\\server\share), optical drive or the name of a remote connection
  adresa TEXT,
  umiestnenie TEXT,
  poznamka TEXT,
  pridany INTEGER NOT NULL,
  posledny_sken INTEGER,
  posledne_pripojenie INTEGER
) STRICT;
CREATE INDEX disk_katalog ON disk(katalog_id);
CREATE INDEX disk_zvazok ON disk(seriove_cislo_zvazku) WHERE seriove_cislo_zvazku IS NOT NULL;

CREATE TABLE priecinok (
  id INTEGER PRIMARY KEY,
  disk_id INTEGER NOT NULL REFERENCES disk(id) ON DELETE CASCADE,
  -- NULL only for the root folder of the disk (its name is '')
  rodic_id INTEGER REFERENCES priecinok(id) ON DELETE CASCADE,
  nazov TEXT NOT NULL,
  zmeneny INTEGER,
  stav INTEGER NOT NULL DEFAULT 0 CHECK (stav IN (0, 1)),
  zmazany INTEGER,
  UNIQUE (rodic_id, nazov),
  CHECK ((rodic_id IS NULL) = (nazov = '')),
  CHECK ((stav = 1) = (zmazany IS NOT NULL))
) STRICT;
CREATE UNIQUE INDEX priecinok_koren ON priecinok(disk_id) WHERE rodic_id IS NULL;
CREATE INDEX priecinok_disk ON priecinok(disk_id);

CREATE TRIGGER priecinok_disk_vlozenie BEFORE INSERT ON priecinok
WHEN NEW.rodic_id IS NOT NULL AND (SELECT disk_id FROM priecinok WHERE id = NEW.rodic_id) IS NOT NEW.disk_id
BEGIN
  SELECT RAISE(ABORT, 'priecinok: rodic je na inom disku');
END;

CREATE TRIGGER priecinok_disk_zmena BEFORE UPDATE OF rodic_id, disk_id ON priecinok
WHEN NEW.rodic_id IS NOT NULL AND (SELECT disk_id FROM priecinok WHERE id = NEW.rodic_id) IS NOT NEW.disk_id
BEGIN
  SELECT RAISE(ABORT, 'priecinok: rodic je na inom disku');
END;

CREATE TABLE subor (
  id INTEGER PRIMARY KEY,
  priecinok_id INTEGER NOT NULL REFERENCES priecinok(id) ON DELETE CASCADE,
  disk_id INTEGER NOT NULL REFERENCES disk(id) ON DELETE CASCADE,
  nazov TEXT NOT NULL CHECK (length(nazov) > 0),
  -- lower case, without the dot ('' when the name has none)
  pripona TEXT NOT NULL DEFAULT '',
  velkost INTEGER NOT NULL CHECK (velkost >= 0),
  zmeneny INTEGER NOT NULL,
  vytvoreny INTEGER,
  atributy INTEGER NOT NULL DEFAULT 0,
  -- size + checksum of the first and last megabyte (16 bytes), for duplicates
  odtlacok BLOB CHECK (odtlacok IS NULL OR length(odtlacok) = 16),
  stav INTEGER NOT NULL DEFAULT 0 CHECK (stav IN (0, 1)),
  prvy_raz INTEGER NOT NULL,
  zmazany INTEGER,
  UNIQUE (priecinok_id, nazov),
  CHECK ((stav = 1) = (zmazany IS NOT NULL))
) STRICT;
CREATE INDEX subor_disk ON subor(disk_id);
CREATE INDEX subor_pripona ON subor(pripona);
CREATE INDEX subor_velkost ON subor(velkost);
CREATE INDEX subor_odtlacok ON subor(odtlacok) WHERE odtlacok IS NOT NULL;

CREATE TRIGGER subor_disk_vlozenie BEFORE INSERT ON subor
WHEN (SELECT disk_id FROM priecinok WHERE id = NEW.priecinok_id) IS NOT NEW.disk_id
BEGIN
  SELECT RAISE(ABORT, 'subor: disk nie je diskom priecinka');
END;

CREATE TRIGGER subor_disk_zmena BEFORE UPDATE OF priecinok_id, disk_id ON subor
WHEN (SELECT disk_id FROM priecinok WHERE id = NEW.priecinok_id) IS NOT NEW.disk_id
BEGIN
  SELECT RAISE(ABORT, 'subor: disk nie je diskom priecinka');
END;

CREATE TABLE medialne_udaje (
  subor_id INTEGER PRIMARY KEY REFERENCES subor(id) ON DELETE CASCADE,
  kontajner TEXT,
  dlzka_ms INTEGER CHECK (dlzka_ms IS NULL OR dlzka_ms >= 0),
  datovy_tok INTEGER,
  video_kodek TEXT,
  sirka INTEGER,
  vyska INTEGER,
  hdr INTEGER NOT NULL DEFAULT 0 CHECK (hdr BETWEEN 0 AND 4),
  snimky REAL,
  trieda_kvality INTEGER NOT NULL DEFAULT 0 CHECK (trieda_kvality BETWEEN 0 AND 4),
  -- problems ffprobe reported (a damaged file goes to review, N3)
  chyby TEXT,
  precitane INTEGER NOT NULL
) STRICT;
CREATE INDEX medialne_kvalita ON medialne_udaje(trieda_kvality);

CREATE TABLE stopa (
  id INTEGER PRIMARY KEY,
  subor_id INTEGER NOT NULL REFERENCES subor(id) ON DELETE CASCADE,
  poradie INTEGER NOT NULL CHECK (poradie >= 0),
  druh INTEGER NOT NULL CHECK (druh BETWEEN 1 AND 3),
  jazyk TEXT,
  kodek TEXT,
  kanaly INTEGER,
  predvolena INTEGER NOT NULL DEFAULT 0 CHECK (predvolena IN (0, 1)),
  vynutena INTEGER NOT NULL DEFAULT 0 CHECK (vynutena IN (0, 1)),
  sdh INTEGER NOT NULL DEFAULT 0 CHECK (sdh IN (0, 1)),
  -- an external subtitle file (.srt next to the video) the track comes from
  externy_subor_id INTEGER REFERENCES subor(id) ON DELETE CASCADE,
  nazov TEXT,
  UNIQUE (subor_id, poradie)
) STRICT;
CREATE INDEX stopa_jazyk ON stopa(druh, jazyk);
CREATE INDEX stopa_externy ON stopa(externy_subor_id) WHERE externy_subor_id IS NOT NULL;

CREATE TABLE nahlad (
  subor_id INTEGER NOT NULL REFERENCES subor(id) ON DELETE CASCADE,
  poradie INTEGER NOT NULL CHECK (poradie >= 0),
  obrazok_id INTEGER NOT NULL REFERENCES obrazok(id) ON DELETE CASCADE,
  cas_ms INTEGER NOT NULL CHECK (cas_ms >= 0),
  PRIMARY KEY (subor_id, poradie)
) STRICT, WITHOUT ROWID;
CREATE INDEX nahlad_obrazok ON nahlad(obrazok_id);

-- ---------------------------------------------------------------- knowledge

CREATE TABLE titul (
  id INTEGER PRIMARY KEY,
  druh INTEGER NOT NULL CHECK (druh IN (1, 2)),
  nazov TEXT NOT NULL CHECK (length(trim(nazov)) > 0),
  original TEXT,
  rok INTEGER CHECK (rok IS NULL OR rok BETWEEN 1870 AND 2200),
  dej TEXT,
  dlzka_min INTEGER CHECK (dlzka_min IS NULL OR dlzka_min > 0),
  hodnotenie_zdroja REAL,
  plagat_id INTEGER REFERENCES obrazok(id) ON DELETE SET NULL,
  -- the wish list (A2): a wanted title, whether or not a version exists
  zelany INTEGER NOT NULL DEFAULT 0 CHECK (zelany IN (0, 1)),
  moje_hodnotenie INTEGER CHECK (moje_hodnotenie IS NULL OR moje_hodnotenie BETWEEN 1 AND 10),
  moja_poznamka TEXT,
  vytvoreny INTEGER NOT NULL,
  zmeneny INTEGER NOT NULL
) STRICT;
CREATE INDEX titul_nazov ON titul(nazov);
CREATE INDEX titul_rok ON titul(rok);
CREATE INDEX titul_plagat ON titul(plagat_id) WHERE plagat_id IS NOT NULL;

CREATE TABLE epizoda (
  id INTEGER PRIMARY KEY,
  titul_id INTEGER NOT NULL REFERENCES titul(id) ON DELETE CASCADE,
  -- season 0 are the specials ("Season 00")
  seria INTEGER NOT NULL CHECK (seria >= 0),
  cislo INTEGER NOT NULL CHECK (cislo >= 0),
  nazov TEXT,
  -- first broadcast, ISO date
  vysielanie TEXT CHECK (vysielanie IS NULL OR date(vysielanie) = vysielanie),
  dej TEXT,
  UNIQUE (titul_id, seria, cislo)
) STRICT;

CREATE TRIGGER epizoda_len_serialu BEFORE INSERT ON epizoda
WHEN (SELECT druh FROM titul WHERE id = NEW.titul_id) IS NOT 2
BEGIN
  SELECT RAISE(ABORT, 'epizoda: titul nie je serial');
END;

CREATE TRIGGER epizoda_presun_len_k_serialu BEFORE UPDATE OF titul_id ON epizoda
WHEN (SELECT druh FROM titul WHERE id = NEW.titul_id) IS NOT 2
BEGIN
  SELECT RAISE(ABORT, 'epizoda: titul nie je serial');
END;

CREATE TRIGGER titul_serial_s_epizodami BEFORE UPDATE OF druh ON titul
WHEN NEW.druh <> 2 AND EXISTS (SELECT 1 FROM epizoda WHERE titul_id = NEW.id)
BEGIN
  SELECT RAISE(ABORT, 'titul: serial s epizodami nemoze byt filmom');
END;

-- a version: one or more files of one film or of one episode (A3); unconfirmed versions form the list "Na potvrdenie"
CREATE TABLE verzia (
  id INTEGER PRIMARY KEY,
  titul_id INTEGER REFERENCES titul(id) ON DELETE CASCADE,
  epizoda_id INTEGER REFERENCES epizoda(id) ON DELETE CASCADE,
  potvrdena INTEGER NOT NULL DEFAULT 0 CHECK (potvrdena IN (0, 1)),
  zhoda REAL CHECK (zhoda IS NULL OR zhoda BETWEEN 0 AND 1),
  poznamka TEXT,
  vytvorena INTEGER NOT NULL,
  CHECK ((titul_id IS NULL) <> (epizoda_id IS NULL))
) STRICT;
CREATE INDEX verzia_titul ON verzia(titul_id) WHERE titul_id IS NOT NULL;
CREATE INDEX verzia_epizoda ON verzia(epizoda_id) WHERE epizoda_id IS NOT NULL;
CREATE INDEX verzia_na_potvrdenie ON verzia(id) WHERE potvrdena = 0;

CREATE TRIGGER verzia_titulu_len_film BEFORE INSERT ON verzia
WHEN NEW.titul_id IS NOT NULL AND (SELECT druh FROM titul WHERE id = NEW.titul_id) IS NOT 1
BEGIN
  SELECT RAISE(ABORT, 'verzia: priamo k titulu patri len film, serial ma verzie epizod');
END;

CREATE TRIGGER verzia_titulu_len_film_zmena BEFORE UPDATE OF titul_id ON verzia
WHEN NEW.titul_id IS NOT NULL AND (SELECT druh FROM titul WHERE id = NEW.titul_id) IS NOT 1
BEGIN
  SELECT RAISE(ABORT, 'verzia: priamo k titulu patri len film, serial ma verzie epizod');
END;

CREATE TRIGGER titul_film_s_verziami BEFORE UPDATE OF druh ON titul
WHEN NEW.druh <> 1 AND EXISTS (SELECT 1 FROM verzia WHERE titul_id = NEW.id)
BEGIN
  SELECT RAISE(ABORT, 'titul: film s verziami nemoze byt serialom');
END;

CREATE TABLE verzia_subor (
  verzia_id INTEGER NOT NULL REFERENCES verzia(id) ON DELETE CASCADE,
  -- a file belongs to at most one version
  subor_id INTEGER NOT NULL UNIQUE REFERENCES subor(id) ON DELETE CASCADE,
  poradie INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (verzia_id, subor_id)
) STRICT, WITHOUT ROWID;

-- a version whose last file left the library (the disk was removed from it) goes too; a file only marked as deleted
-- keeps its version in the history
CREATE TRIGGER verzia_bez_suborov AFTER DELETE ON verzia_subor
WHEN NOT EXISTS (SELECT 1 FROM verzia_subor WHERE verzia_id = OLD.verzia_id)
BEGIN
  DELETE FROM verzia WHERE id = OLD.verzia_id;
END;

CREATE TABLE kolekcia (
  id INTEGER PRIMARY KEY,
  nazov TEXT NOT NULL CHECK (length(trim(nazov)) > 0),
  dej TEXT,
  plagat_id INTEGER REFERENCES obrazok(id) ON DELETE SET NULL,
  vytvorena INTEGER NOT NULL
) STRICT;

CREATE TABLE kolekcia_titul (
  kolekcia_id INTEGER NOT NULL REFERENCES kolekcia(id) ON DELETE CASCADE,
  titul_id INTEGER NOT NULL REFERENCES titul(id) ON DELETE CASCADE,
  poradie INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (kolekcia_id, titul_id)
) STRICT, WITHOUT ROWID;
CREATE INDEX kolekcia_titul_titul ON kolekcia_titul(titul_id);

CREATE TABLE osoba (
  id INTEGER PRIMARY KEY,
  meno TEXT NOT NULL CHECK (length(trim(meno)) > 0),
  fotka_id INTEGER REFERENCES obrazok(id) ON DELETE SET NULL
) STRICT;

CREATE TABLE titul_osoba (
  titul_id INTEGER NOT NULL REFERENCES titul(id) ON DELETE CASCADE,
  osoba_id INTEGER NOT NULL REFERENCES osoba(id) ON DELETE CASCADE,
  rola INTEGER NOT NULL CHECK (rola BETWEEN 1 AND 3),
  postava TEXT,
  poradie INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (titul_id, osoba_id, rola)
) STRICT, WITHOUT ROWID;
CREATE INDEX titul_osoba_osoba ON titul_osoba(osoba_id);

CREATE TABLE zaner (
  id INTEGER PRIMARY KEY,
  nazov TEXT NOT NULL UNIQUE COLLATE NOCASE
) STRICT;

CREATE TABLE titul_zaner (
  titul_id INTEGER NOT NULL REFERENCES titul(id) ON DELETE CASCADE,
  zaner_id INTEGER NOT NULL REFERENCES zaner(id) ON DELETE CASCADE,
  PRIMARY KEY (titul_id, zaner_id)
) STRICT, WITHOUT ROWID;
CREATE INDEX titul_zaner_zaner ON titul_zaner(zaner_id);

CREATE TABLE stitok (
  id INTEGER PRIMARY KEY,
  nazov TEXT NOT NULL UNIQUE COLLATE NOCASE CHECK (length(trim(nazov)) > 0),
  farba TEXT
) STRICT;

CREATE TABLE titul_stitok (
  titul_id INTEGER NOT NULL REFERENCES titul(id) ON DELETE CASCADE,
  stitok_id INTEGER NOT NULL REFERENCES stitok(id) ON DELETE CASCADE,
  PRIMARY KEY (titul_id, stitok_id)
) STRICT, WITHOUT ROWID;
CREATE INDEX titul_stitok_stitok ON titul_stitok(stitok_id);

-- identifiers of the sources (TMDB "movie/603", HISTRA, Jellyfin, IMDb); exactly one object column is set, by kind
CREATE TABLE identifikator (
  zdroj INTEGER NOT NULL CHECK (zdroj BETWEEN 3 AND 6),
  druh INTEGER NOT NULL CHECK (druh BETWEEN 1 AND 4),
  hodnota TEXT NOT NULL CHECK (length(hodnota) > 0),
  titul_id INTEGER REFERENCES titul(id) ON DELETE CASCADE,
  epizoda_id INTEGER REFERENCES epizoda(id) ON DELETE CASCADE,
  kolekcia_id INTEGER REFERENCES kolekcia(id) ON DELETE CASCADE,
  osoba_id INTEGER REFERENCES osoba(id) ON DELETE CASCADE,
  PRIMARY KEY (zdroj, druh, hodnota),
  CHECK ((druh = 1) = (titul_id IS NOT NULL)
     AND (druh = 2) = (epizoda_id IS NOT NULL)
     AND (druh = 3) = (kolekcia_id IS NOT NULL)
     AND (druh = 4) = (osoba_id IS NOT NULL))
) STRICT, WITHOUT ROWID;
CREATE INDEX identifikator_titul ON identifikator(titul_id) WHERE titul_id IS NOT NULL;
CREATE INDEX identifikator_epizoda ON identifikator(epizoda_id) WHERE epizoda_id IS NOT NULL;
CREATE INDEX identifikator_kolekcia ON identifikator(kolekcia_id) WHERE kolekcia_id IS NOT NULL;
CREATE INDEX identifikator_osoba ON identifikator(osoba_id) WHERE osoba_id IS NOT NULL;

-- origin of each field (A5): which source wrote it and when; a field the user changed is locked
CREATE TABLE povod_pola (
  druh INTEGER NOT NULL CHECK (druh BETWEEN 1 AND 4),
  objekt_id INTEGER NOT NULL,
  pole TEXT NOT NULL,
  zdroj INTEGER NOT NULL CHECK (zdroj BETWEEN 0 AND 6),
  cas INTEGER NOT NULL,
  zamknute INTEGER NOT NULL DEFAULT 0 CHECK (zamknute IN (0, 1)),
  PRIMARY KEY (druh, objekt_id, pole)
) STRICT, WITHOUT ROWID;

CREATE TRIGGER povod_titulu AFTER DELETE ON titul BEGIN
  DELETE FROM povod_pola WHERE druh = 1 AND objekt_id = OLD.id;
END;
CREATE TRIGGER povod_epizody AFTER DELETE ON epizoda BEGIN
  DELETE FROM povod_pola WHERE druh = 2 AND objekt_id = OLD.id;
END;
CREATE TRIGGER povod_kolekcie AFTER DELETE ON kolekcia BEGIN
  DELETE FROM povod_pola WHERE druh = 3 AND objekt_id = OLD.id;
END;
CREATE TRIGGER povod_osoby AFTER DELETE ON osoba BEGIN
  DELETE FROM povod_pola WHERE druh = 4 AND objekt_id = OLD.id;
END;

CREATE TABLE histra_zoznam (
  id INTEGER PRIMARY KEY,
  histra_id TEXT NOT NULL UNIQUE,
  nazov TEXT NOT NULL,
  synchronizovany INTEGER
) STRICT;

CREATE TABLE histra_polozka (
  zoznam_id INTEGER NOT NULL REFERENCES histra_zoznam(id) ON DELETE CASCADE,
  histra_titul TEXT NOT NULL,
  nazov TEXT NOT NULL,
  rok INTEGER,
  -- the title in the library ("mám"); NULL when the library does not know it
  titul_id INTEGER REFERENCES titul(id) ON DELETE SET NULL,
  PRIMARY KEY (zoznam_id, histra_titul)
) STRICT, WITHOUT ROWID;
CREATE INDEX histra_polozka_titul ON histra_polozka(titul_id) WHERE titul_id IS NOT NULL;

-- ---------------------------------------------------------------- use

-- watched: tied to the title or episode, never to a file, so it survives replacing a version with a better one
CREATE TABLE pozretie (
  id INTEGER PRIMARY KEY,
  titul_id INTEGER REFERENCES titul(id) ON DELETE CASCADE,
  epizoda_id INTEGER REFERENCES epizoda(id) ON DELETE CASCADE,
  kedy INTEGER NOT NULL,
  CHECK ((titul_id IS NULL) <> (epizoda_id IS NULL))
) STRICT;
CREATE INDEX pozretie_titul ON pozretie(titul_id) WHERE titul_id IS NOT NULL;
CREATE INDEX pozretie_epizoda ON pozretie(epizoda_id) WHERE epizoda_id IS NOT NULL;

CREATE TABLE playlist (
  id INTEGER PRIMARY KEY,
  nazov TEXT NOT NULL CHECK (length(trim(nazov)) > 0),
  druh INTEGER NOT NULL CHECK (druh BETWEEN 1 AND 3),
  -- smart and random playlists are a query of the one query language (A4)
  dotaz TEXT,
  vytvoreny INTEGER NOT NULL,
  CHECK (druh = 1 OR dotaz IS NOT NULL)
) STRICT;

CREATE TABLE playlist_polozka (
  playlist_id INTEGER NOT NULL REFERENCES playlist(id) ON DELETE CASCADE,
  poradie INTEGER NOT NULL CHECK (poradie >= 0),
  titul_id INTEGER REFERENCES titul(id) ON DELETE CASCADE,
  epizoda_id INTEGER REFERENCES epizoda(id) ON DELETE CASCADE,
  -- the preferred version, if the user picked one
  verzia_id INTEGER REFERENCES verzia(id) ON DELETE SET NULL,
  PRIMARY KEY (playlist_id, poradie),
  CHECK ((titul_id IS NULL) <> (epizoda_id IS NULL))
) STRICT, WITHOUT ROWID;
CREATE INDEX playlist_polozka_titul ON playlist_polozka(titul_id) WHERE titul_id IS NOT NULL;
CREATE INDEX playlist_polozka_epizoda ON playlist_polozka(epizoda_id) WHERE epizoda_id IS NOT NULL;
CREATE INDEX playlist_polozka_verzia ON playlist_polozka(verzia_id) WHERE verzia_id IS NOT NULL;

CREATE TABLE ulozene_hladanie (
  id INTEGER PRIMARY KEY,
  nazov TEXT NOT NULL UNIQUE COLLATE NOCASE CHECK (length(trim(nazov)) > 0),
  dotaz TEXT NOT NULL,
  v_paneli INTEGER NOT NULL DEFAULT 1 CHECK (v_paneli IN (0, 1)),
  poradie INTEGER NOT NULL DEFAULT 0
) STRICT;

CREATE TABLE vypozicka (
  id INTEGER PRIMARY KEY,
  komu TEXT NOT NULL CHECK (length(trim(komu)) > 0),
  od INTEGER NOT NULL,
  -- NULL while it is lent
  do INTEGER,
  disk_id INTEGER REFERENCES disk(id) ON DELETE CASCADE,
  titul_id INTEGER REFERENCES titul(id) ON DELETE CASCADE,
  poznamka TEXT,
  CHECK ((disk_id IS NULL) <> (titul_id IS NULL)),
  CHECK (do IS NULL OR do >= od)
) STRICT;
CREATE INDEX vypozicka_disk ON vypozicka(disk_id) WHERE disk_id IS NOT NULL;
CREATE INDEX vypozicka_titul ON vypozicka(titul_id) WHERE titul_id IS NOT NULL;

-- ---------------------------------------------------------------- system

-- the persistent job queue (A7): each lane ("disk:12", "net:tmdb") runs on its own
CREATE TABLE uloha (
  id INTEGER PRIMARY KEY,
  druh INTEGER NOT NULL CHECK (druh BETWEEN 1 AND 7),
  stav INTEGER NOT NULL DEFAULT 0 CHECK (stav BETWEEN 0 AND 5),
  priorita INTEGER NOT NULL DEFAULT 0,
  pruh TEXT NOT NULL CHECK (length(pruh) > 0),
  disk_id INTEGER REFERENCES disk(id) ON DELETE CASCADE,
  parametre TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(parametre)),
  priebeh REAL NOT NULL DEFAULT 0 CHECK (priebeh BETWEEN 0 AND 1),
  chyba TEXT,
  pokusy INTEGER NOT NULL DEFAULT 0 CHECK (pokusy >= 0),
  vytvorena INTEGER NOT NULL,
  zacala INTEGER,
  skoncila INTEGER
) STRICT;
CREATE INDEX uloha_na_rade ON uloha(pruh, priorita DESC, id) WHERE stav IN (0, 5);
CREATE INDEX uloha_disk ON uloha(disk_id) WHERE disk_id IS NOT NULL;

CREATE TABLE sken (
  id INTEGER PRIMARY KEY,
  disk_id INTEGER NOT NULL REFERENCES disk(id) ON DELETE CASCADE,
  zacal INTEGER NOT NULL,
  skoncil INTEGER,
  pridane INTEGER NOT NULL DEFAULT 0,
  zmenene INTEGER NOT NULL DEFAULT 0,
  zmazane INTEGER NOT NULL DEFAULT 0,
  chyby INTEGER NOT NULL DEFAULT 0,
  stav INTEGER NOT NULL CHECK (stav BETWEEN 0 AND 5)
) STRICT;
CREATE INDEX sken_disk ON sken(disk_id, zacal);

CREATE TABLE zdravie_zaznam (
  id INTEGER PRIMARY KEY,
  disk_id INTEGER NOT NULL REFERENCES disk(id) ON DELETE CASCADE,
  kedy INTEGER NOT NULL,
  teplota INTEGER,
  hodiny INTEGER,
  realokovane INTEGER,
  cakajuce INTEGER,
  -- wear of an SSD in percent
  opotrebenie INTEGER CHECK (opotrebenie IS NULL OR opotrebenie BETWEEN 0 AND 100),
  stav INTEGER NOT NULL DEFAULT 0 CHECK (stav BETWEEN 0 AND 3),
  surove TEXT CHECK (surove IS NULL OR json_valid(surove))
) STRICT;
CREATE INDEX zdravie_disk ON zdravie_zaznam(disk_id, kedy);

-- ---------------------------------------------------------------- full-text index

-- one index for every name in the library; the row id is id * 8 + kind (Katalog.Jadro.Model.KlucHladania), so the
-- names are not stored twice (contentless). Trigram tokens find any part of a word, with or without diacritics.
CREATE VIRTUAL TABLE hladanie USING fts5(
  text,
  content = '',
  contentless_delete = 1,
  tokenize = 'trigram remove_diacritics 1'
);

CREATE TRIGGER hladanie_subor_novy AFTER INSERT ON subor BEGIN
  INSERT INTO hladanie(rowid, text) VALUES (NEW.id * 8 + 1, NEW.nazov);
END;
CREATE TRIGGER hladanie_subor_zmena AFTER UPDATE OF nazov ON subor BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 1;
  INSERT INTO hladanie(rowid, text) VALUES (NEW.id * 8 + 1, NEW.nazov);
END;
CREATE TRIGGER hladanie_subor_koniec AFTER DELETE ON subor BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 1;
END;

CREATE TRIGGER hladanie_priecinok_novy AFTER INSERT ON priecinok WHEN NEW.nazov <> '' BEGIN
  INSERT INTO hladanie(rowid, text) VALUES (NEW.id * 8 + 2, NEW.nazov);
END;
CREATE TRIGGER hladanie_priecinok_zmena AFTER UPDATE OF nazov ON priecinok BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 2;
  INSERT INTO hladanie(rowid, text) SELECT NEW.id * 8 + 2, NEW.nazov WHERE NEW.nazov <> '';
END;
CREATE TRIGGER hladanie_priecinok_koniec AFTER DELETE ON priecinok BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 2;
END;

CREATE TRIGGER hladanie_titul_novy AFTER INSERT ON titul BEGIN
  INSERT INTO hladanie(rowid, text) VALUES (NEW.id * 8 + 3, NEW.nazov || ' ' || coalesce(NEW.original, ''));
END;
CREATE TRIGGER hladanie_titul_zmena AFTER UPDATE OF nazov, original ON titul BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 3;
  INSERT INTO hladanie(rowid, text) VALUES (NEW.id * 8 + 3, NEW.nazov || ' ' || coalesce(NEW.original, ''));
END;
CREATE TRIGGER hladanie_titul_koniec AFTER DELETE ON titul BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 3;
END;

CREATE TRIGGER hladanie_epizoda_novy AFTER INSERT ON epizoda WHEN NEW.nazov IS NOT NULL BEGIN
  INSERT INTO hladanie(rowid, text) VALUES (NEW.id * 8 + 4, NEW.nazov);
END;
CREATE TRIGGER hladanie_epizoda_zmena AFTER UPDATE OF nazov ON epizoda BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 4;
  INSERT INTO hladanie(rowid, text) SELECT NEW.id * 8 + 4, NEW.nazov WHERE NEW.nazov IS NOT NULL;
END;
CREATE TRIGGER hladanie_epizoda_koniec AFTER DELETE ON epizoda BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 4;
END;

CREATE TRIGGER hladanie_osoba_novy AFTER INSERT ON osoba BEGIN
  INSERT INTO hladanie(rowid, text) VALUES (NEW.id * 8 + 5, NEW.meno);
END;
CREATE TRIGGER hladanie_osoba_zmena AFTER UPDATE OF meno ON osoba BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 5;
  INSERT INTO hladanie(rowid, text) VALUES (NEW.id * 8 + 5, NEW.meno);
END;
CREATE TRIGGER hladanie_osoba_koniec AFTER DELETE ON osoba BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 5;
END;

CREATE TRIGGER hladanie_kolekcia_novy AFTER INSERT ON kolekcia BEGIN
  INSERT INTO hladanie(rowid, text) VALUES (NEW.id * 8 + 6, NEW.nazov);
END;
CREATE TRIGGER hladanie_kolekcia_zmena AFTER UPDATE OF nazov ON kolekcia BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 6;
  INSERT INTO hladanie(rowid, text) VALUES (NEW.id * 8 + 6, NEW.nazov);
END;
CREATE TRIGGER hladanie_kolekcia_koniec AFTER DELETE ON kolekcia BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 6;
END;

CREATE TRIGGER hladanie_disk_novy AFTER INSERT ON disk BEGIN
  INSERT INTO hladanie(rowid, text) VALUES (NEW.id * 8 + 7, NEW.nazov || ' ' || coalesce(NEW.menovka, ''));
END;
CREATE TRIGGER hladanie_disk_zmena AFTER UPDATE OF nazov, menovka ON disk BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 7;
  INSERT INTO hladanie(rowid, text) VALUES (NEW.id * 8 + 7, NEW.nazov || ' ' || coalesce(NEW.menovka, ''));
END;
CREATE TRIGGER hladanie_disk_koniec AFTER DELETE ON disk BEGIN
  DELETE FROM hladanie WHERE rowid = OLD.id * 8 + 7;
END;
