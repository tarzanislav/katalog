-- KATALOG library, schema version 5 (stage E6, 8 Nov 2026).
--
-- * Tags of titles and the names of borrowers keep their text without diacritics and letter case, for the query
--   language (stitok:komedia-vecer, pozicane:jano); the program fills it in when it writes them.
-- * A disk or a title is lent to one person at a time: at most one open loan each.

ALTER TABLE stitok ADD COLUMN kluc TEXT NOT NULL DEFAULT '';
UPDATE stitok SET kluc = lower(trim(nazov));

ALTER TABLE vypozicka ADD COLUMN kluc TEXT NOT NULL DEFAULT '';
UPDATE vypozicka SET kluc = lower(trim(komu));

CREATE UNIQUE INDEX vypozicka_disk_otvorena ON vypozicka(disk_id) WHERE do IS NULL AND disk_id IS NOT NULL;
CREATE UNIQUE INDEX vypozicka_titul_otvorena ON vypozicka(titul_id) WHERE do IS NULL AND titul_id IS NOT NULL;
