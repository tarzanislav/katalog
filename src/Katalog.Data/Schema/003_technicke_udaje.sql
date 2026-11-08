-- KATALOG library, schema version 3 (stage E3, 8 Nov 2026).
--
-- * Tracks are searched by their language (zvuk:cs, titulky:sk): an index by kind and language leads to the files.
-- * The job queue is read by state when the program starts and when a disk is connected.

CREATE INDEX stopa_jazyk ON stopa(druh, jazyk, subor_id);
CREATE INDEX uloha_stav ON uloha(stav, disk_id);
