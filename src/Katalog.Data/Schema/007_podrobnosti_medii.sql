-- KATALOG library, schema version 7 (owner, 8 Nov 2026).
--
-- * The details of a video as KATALOG's own reader of Matroska and MP4 files finds them (format and its version,
--   profile, bit depth, colours, the size and data rate of each track, …), kept as JSON sections for the detail
--   panel. NULL = not read by the own reader yet: MKV and MP4 files read by ffprobe before are read once more (fast);
--   '[]' = the own reader cannot read the file (ffprobe's data stay).

ALTER TABLE medialne_udaje ADD COLUMN podrobnosti TEXT;
