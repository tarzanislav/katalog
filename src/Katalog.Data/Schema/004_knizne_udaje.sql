-- KATALOG library, schema version 4 (stage E4, 8 Nov 2026).
--
-- * Titles: when the sources were last searched for a title and when its data were last loaded (so a source is not asked
--   again and again), the keys of all its names (a file named in Slovak or in the original finds the same title), and
--   the candidates the sources offered for a title that waits for confirmation ("Na potvrdenie"), kept so the window
--   shows them offline.
-- * Files that are no titles: extras and samples the recognition found, and those the user said (home videos, trailers
--   kept on purpose); recognition leaves them alone.
-- * Genres keep their name without diacritics (the query language searches by it).
-- * A file with several episodes ("S01E01E02"): its version belongs to the first, the others are listed here.

ALTER TABLE titul ADD COLUMN hladany INTEGER;
ALTER TABLE titul ADD COLUMN nacitany INTEGER;

-- a genre's name without diacritics and letter case ("komedia"), for zaner:komedia
ALTER TABLE zaner ADD COLUMN kluc TEXT;

-- "1:pan prstenov navrat krala:2003" (Katalog.Jadro.Model.Normalizacia.Kluc)
CREATE TABLE titul_kluc (
  kluc TEXT NOT NULL CHECK (length(kluc) > 0),
  titul_id INTEGER NOT NULL REFERENCES titul(id) ON DELETE CASCADE,
  PRIMARY KEY (kluc, titul_id)
) STRICT, WITHOUT ROWID;
CREATE INDEX titul_kluc_titul ON titul_kluc(titul_id);

CREATE TABLE kandidat (
  titul_id INTEGER NOT NULL REFERENCES titul(id) ON DELETE CASCADE,
  zdroj INTEGER NOT NULL CHECK (zdroj BETWEEN 3 AND 6),
  identifikator TEXT NOT NULL CHECK (length(identifikator) > 0),
  druh INTEGER NOT NULL CHECK (druh IN (1, 2)),
  nazov TEXT NOT NULL,
  original TEXT,
  rok INTEGER,
  zhoda REAL NOT NULL CHECK (zhoda BETWEEN 0 AND 1),
  plagat TEXT,
  poradie INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (titul_id, zdroj, identifikator)
) STRICT, WITHOUT ROWID;

CREATE TABLE subor_mimo_kniznice (
  subor_id INTEGER PRIMARY KEY REFERENCES subor(id) ON DELETE CASCADE,
  -- 1 = the user said so (kept for ever); 0 = the recognition (a newer program may look at it again)
  pouzivatel INTEGER NOT NULL DEFAULT 0 CHECK (pouzivatel IN (0, 1))
) STRICT;

CREATE TABLE verzia_dalsia_epizoda (
  verzia_id INTEGER NOT NULL REFERENCES verzia(id) ON DELETE CASCADE,
  epizoda_id INTEGER NOT NULL REFERENCES epizoda(id) ON DELETE CASCADE,
  PRIMARY KEY (verzia_id, epizoda_id)
) STRICT, WITHOUT ROWID;
CREATE INDEX verzia_dalsia_epizoda_epizoda ON verzia_dalsia_epizoda(epizoda_id);
