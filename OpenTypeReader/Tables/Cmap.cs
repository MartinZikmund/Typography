﻿//Apache2, 2014-2016, Samuel Carlsson, WinterDev

using System;
using System.Collections.Generic;
using System.IO;
namespace NRasterizer.Tables
{
    ////////////////////////////////////////////////////////////////////////
    //from https://www.microsoft.com/typography/developers/opentype/detail.htm
    //CMAP Table
    //Every glyph in a TrueType font is identified by a unique Glyph ID (GID),
    //a simple sequential numbering of all the glyphs in the font. 
    //These GIDs are mapped to character codepoints in the font's CMAP table.
    //In OpenType fonts, the principal mapping is to Unicode codepoints; that is, 
    //the GIDs of nominal glyph representations of specific characters are mapped to appropriate Unicode values.

    //The key to OpenType glyph processing is that not every glyph in a font is directly mapped to a codepoint. 
    //Variant glyph forms, ligatures, dynamically composed diacritics and other rendering forms do not require entries in the CMAP table. 
    //Rather, their GIDs are mapped in layout features to the GIDs of nominal character forms, 
    //i.e. to those glyphs that do have CMAP entries. This is the heart of glyph processing: the mapping of GIDs to each other, 
    //rather than directly to character codepoints.

    //In order for fonts to be able to correctly render text, 
    //font developers must ensure that the correct nominal glyph form GIDs are mapped to the correct Unicode codepoints. 
    //Application developers, of course, must ensure that their applications correctly manage input and storage of Unicode text codepoints,
    //or map correctly to these codepoints from other codepages and character sets. 
    ////////////////////////////////////////////////////////////////////////

    class Cmap : TableEntry
    {
        CharacterMap[] charMaps;
        public override string Name
        {
            get { return "cmap"; }
        }
        public CharacterMap[] CharMaps
        {
            get { return charMaps; }
        }
        protected override void ReadContentFrom(BinaryReader input)
        {
            //https://www.microsoft.com/typography/otspec/cmap.htm
            long beginAt = input.BaseStream.Position;
            //
            ushort version = input.ReadUInt16(); // 0
            ushort tableCount = input.ReadUInt16();

            var entries = new CMapEntry[tableCount];
            for (int i = 0; i < tableCount; i++)
            {
                ushort platformId = input.ReadUInt16();
                ushort encodingId = input.ReadUInt16();
                uint offset = input.ReadUInt32();
                entries[i] = new CMapEntry(platformId, encodingId, offset);
            }

            charMaps = new CharacterMap[tableCount];
            for (int i = 0; i < tableCount; i++)
            {
                CMapEntry entry = entries[i];
                input.BaseStream.Seek(beginAt + entry.Offset, SeekOrigin.Begin);
                CharacterMap cmap = charMaps[i] = ReadCharacterMap(entry, input);
                cmap.PlatformId = entry.PlatformId;
                cmap.EncodingId = entry.EncodingId;
            }
        }
        static CharacterMap ReadCharacterMap(CMapEntry entry, BinaryReader input)
        {

            ushort format = input.ReadUInt16();
            ushort length = input.ReadUInt16();
            switch (format)
            {
                default:
                    {
                        throw new ApplicationException("Unknown cmap subtable: " + format); // TODO: Replace all applicationexceptions
                    }
                case 4:
                    {
                        //This is the Microsoft standard character to glyph index mapping table for fonts that support Unicode ranges other than the range [U+D800 - U+DFFF] (defined as Surrogates Area, in Unicode v 3.0) 
                        //which is used for UCS-4 characters.
                        //If a font supports this character range (i.e. in turn supports the UCS-4 characters) a subtable in this format with a platform specific encoding ID 1 is yet needed,
                        //in addition to a subtable in format 12 with a platform specific encoding ID 10. Please see details on format 12 below, for fonts that support UCS-4 characters on Windows.
                        //  
                        //This format is used when the character codes for the characters represented by a font fall into several contiguous ranges, 
                        //possibly with holes in some or all of the ranges (that is, some of the codes in a range may not have a representation in the font). 
                        //The format-dependent data is divided into three parts, which must occur in the following order:
                        //    A four-word header gives parameters for an optimized search of the segment list;
                        //    Four parallel arrays describe the segments (one segment for each contiguous range of codes);
                        //    A variable-length array of glyph IDs (unsigned words).
                        long tableStartEndAt = input.BaseStream.Position + length;

                        ushort language = input.ReadUInt16();
                        //Note on the language field in 'cmap' subtables:
                        //Note on the language field in 'cmap' subtables: 
                        //The language field must be set to zero for all cmap subtables whose platform IDs are other than Macintosh (platform ID 1).
                        //For cmap subtables whose platform IDs are Macintosh, set this field to the Macintosh language ID of the cmap subtable plus one, 
                        //or to zero if the cmap subtable is not language-specific.
                        //For example, a Mac OS Turkish cmap subtable must set this field to 18, since the Macintosh language ID for Turkish is 17. 
                        //A Mac OS Roman cmap subtable must set this field to 0, since Mac OS Roman is not a language-specific encoding.

                        ushort segCountX2 = input.ReadUInt16(); //2 * segCount
                        ushort searchRange = input.ReadUInt16(); //2 * (2**FLOOR(log2(segCount)))
                        ushort entrySelector = input.ReadUInt16();//2 * (2**FLOOR(log2(segCount)))
                        ushort rangeShift = input.ReadUInt16(); //2 * (2**FLOOR(log2(segCount)))
                        int segCount = segCountX2 / 2;
                        ushort[] endCode = Utils.ReadUInt16Array(input, segCount);//Ending character code for each segment, last = 0xFFFF.            
                        //>To ensure that the search will terminate, the final endCode value must be 0xFFFF.
                        //>This segment need not contain any valid mappings. It can simply map the single character code 0xFFFF to the missing character glyph, glyph 0.

                        input.ReadUInt16(); // Reserved = 0               
                        ushort[] startCode = Utils.ReadUInt16Array(input, segCount); //Starting character code for each segment
                        ushort[] idDelta = Utils.ReadUInt16Array(input, segCount); //Delta for all character codes in segment
                        ushort[] idRangeOffset = Utils.ReadUInt16Array(input, segCount); //Offset in bytes to glyph indexArray, or 0   
                        //------------------------------------------------------------------------------------ 
                        long remainingLen = tableStartEndAt - input.BaseStream.Position;
                        int recordNum2 = (int)(remainingLen / 2);
                        ushort[] glyphIdArray = Utils.ReadUInt16Array(input, recordNum2);//Glyph index array 
                        return new CharacterMap(segCount, startCode, endCode, idDelta, idRangeOffset, glyphIdArray);
                    }
            }
        }

        //static int FindGlyphIdArrayLenInBytes(ushort[] idRangeOffset)
        //{
        //    //1. find max OffsetValue (in bytes unit)
        //    //this is the possible value to reach from the idRangeOffsetRecord 
        //    ushort max = 0;
        //    int foundAt = 0;
        //    for (int i = idRangeOffset.Length - 1; i >= 0; --i)
        //    {
        //        ushort off = idRangeOffset[i];
        //        if (off > max)
        //        {
        //            max = off;
        //            foundAt = i;
        //        }
        //    }
        //    //----------------------------
        //    //2. then offset with current found record
        //    return max - (foundAt * 2); //*2 = to byte unit 
        //}
        struct CMapEntry
        {
            readonly UInt16 _platformId;
            readonly UInt16 _encodingId;
            readonly UInt32 _offset;
            public CMapEntry(UInt16 platformId, UInt16 encodingId, UInt32 offset)
            {
                _platformId = platformId;
                _encodingId = encodingId;
                _offset = offset;
            }
            public UInt16 PlatformId { get { return _platformId; } }
            public UInt16 EncodingId { get { return _encodingId; } }
            public UInt32 Offset { get { return _offset; } }
        }
    }
}