-- KATALOG library, schema version 4 (stage E4, 8 Nov 2026).
--
-- * Titles: when the sources were last asked about a title (so it is not asked again and again), and the candidates the
--   sources offered for a title that waits for confirmation ("Na potvrdenie"), kept so the window shows them offline.
-- * Files the user said are no titles (home videos, trailers kept on purpose): recognition leaves them alone.
-- * A file with several episodes ("S01E01E02"): its version belongs to the first, the others are listed here.

ALTER TABLE titul ADD COLUMN hladany INTEGER;

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
  subor_id INTEGER PRIMARY KEY REFERENCES subor(id) ON DELETE CASCADE
) STRICT;

CREATE TABLE verzia_epizoda (
  verzia_id INTEGER NOT NULL REFERENCES verzia(id) ON DELETE CASCADE,
  epizoda_id INTEGER NOT NULL REFERENCES epizoda(id) ON DELETE CASCADE,
  PRIMARY KEY (verzia_id, epizoda_id)
) STRICT, WITHOUT ROWID;
CREATE INDEX verzia_epizoda_epizoda ON verzia_epizoda(epizoda_id);
