-- KATALOG library, schema version 2 (stage E1, 8 Nov 2026).
--
-- * A disk of the catalogue may be a whole volume or one folder of it (e.g. D:\Filmy): "koren" is the folder's path inside
--   the volume, '' for the whole volume. The identity of the volume stays the same, so the disk is still recognised by its
--   volume, never by its drive letter.
-- * The index of file sizes is dropped: duplicates are found by the fingerprint (it includes the size), and a size filter
--   reads the files of the chosen disks anyway. Without it a scan writes about a sixth faster.

ALTER TABLE disk ADD COLUMN koren TEXT NOT NULL DEFAULT '';

DROP INDEX subor_velkost;
