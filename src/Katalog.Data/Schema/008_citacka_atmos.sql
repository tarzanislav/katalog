-- KATALOG library, schema version 8 (9 Nov 2026).
--
-- * The own reader of MKV and MP4 now looks into the first frames too (Dolby Atmos in TrueHD and E-AC-3, DTS:X,
--   HDR10+ and Dolby Vision in the picture, the dialogue level and channel layout of Dolby Digital): the details read
--   before are read once more (fast, kilobytes per file).

UPDATE medialne_udaje SET podrobnosti = NULL WHERE podrobnosti IS NOT NULL;
