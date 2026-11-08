-- KATALOG library, schema version 6 (stage E7, 8 Nov 2026).
--
-- * A random playlist draws a number of titles from its query; the seed keeps the draw the same until the user draws
--   again ("Vylosovať znova").

ALTER TABLE playlist ADD COLUMN pocet INTEGER CHECK (pocet IS NULL OR pocet BETWEEN 1 AND 1000);
ALTER TABLE playlist ADD COLUMN semienko INTEGER NOT NULL DEFAULT 0;
