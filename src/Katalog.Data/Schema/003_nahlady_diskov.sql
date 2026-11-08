-- KATALOG library, schema version 3 (stage E3, corrected at the start of E4, 8 Nov 2026).
--
-- Previews of videos are made only for disks the user chose (R10: off by default, switched on per disk).

ALTER TABLE disk ADD COLUMN nahlady INTEGER NOT NULL DEFAULT 0 CHECK (nahlady IN (0, 1));
