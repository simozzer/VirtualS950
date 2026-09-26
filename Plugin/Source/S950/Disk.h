#pragma once

#include "Patch.h"

#include <cstdint>
#include <string>
#include <vector>

namespace s950
{
    /*
     * An 800K Akai S900/S950 floppy: 80 cylinders x 2 heads x 5 sectors of 1024 bytes.
     *
     * Block 0 holds a 64-entry directory at 0x000 and a 16-bit allocation table at 0x600;
     * file data starts at block 4. A file's blocks are a chain through that table rather
     * than a run, so a file is not contiguous and cannot be read as one.
     *
     * The reading half of AkaiS950List, ported. Nothing here writes: a plugin opens disks
     * and plays them, and every way of damaging one lives in the editor where a human is
     * watching. Notably absent for that reason are the rules that make writing dangerous -
     * that the directory must stay contiguous, and that zone pointers are positions that
     * have to be recomputed rather than shifted.
     */
    class Disk
    {
    public:
        static constexpr int BlockSize  = 1024;
        static constexpr int DirOffset  = 0x000;
        static constexpr int DirEntries = 64;
        static constexpr int EntrySize  = 24;
        static constexpr int FatOffset  = 0x600;
        static constexpr int FatEnd     = 0x8000;   // end-of-chain marker
        static constexpr int HeaderSize = 60;       // per-file header before the payload

        static constexpr int ProgHeaderSize     = 38;
        static constexpr int KeygroupSize       = 70;
        static constexpr int KeygroupNameOffset = 24;   // zone 1's sample name
        static constexpr int KeygroupZoneStride = 22;   // zone 2's is 22 bytes further on

        /// One 24-byte entry in the directory.
        struct Entry
        {
            int         slot = 0;
            std::string name;
            char        type = 0;        // 'P' program, 'S' sample, 'D' drum set, 'O' overall
            int         length = 0;      // bytes, including the 60-byte file header
            int         startBlock = 0;
            int         chainBlocks = 0;
            bool        chainOk = false; // the allocation covers the declared length

            // Samples only, from the file's own 60-byte header.
            long long sampleCount = 0;
            int       sampleRate = 0;
            int       tuning = 0;        // 16ths of a semitone, C3 = 960
            char      loopMode = 'O';    // 'O' one-shot, 'L' loop, 'A' alternating
            char      loopDirection = 'N';
            long long loopStart = 0, loopEnd = 0, loopLength = 0;
            int       loudness = 0;      // signed

            int    nominalPitch() const { return tuning / 16; }
            int    finePitch()    const { return tuning % 16; }
            double seconds()      const { return sampleRate > 0 ? (double) sampleCount / sampleRate : 0.0; }
        };

        /// One zone of a keygroup: which sample, and how it is trimmed.
        struct Zone
        {
            std::string name;
            int         pointer = 0;
            int         fine = 0;
            int         transpose = 0;   // signed
            int         filter = 99;
            int         loudness = 0;    // signed

            double pitchOffset() const { return transpose + fine / 256.0; }

            /*
             * "2 SAMPLE" is what the panel leaves in a zone it is not using, and a pointer
             * of zero says the same thing. Reading those as a sample name is how the editor
             * came to show leftovers as though a second zone were loaded.
             */
            bool inUse() const { return ! name.empty() && name != "2 SAMPLE" && pointer != 0; }
        };

        /// One 70-byte keygroup record.
        struct Keygroup
        {
            int index = 0;
            int highKey = 127, lowKey = 0;
            int velocitySwitch = 128;

            int vcaAttack = 0, vcaDecay = 0, vcaSustain = 99, vcaRelease = 0;
            int vcfAttack = 0, vcfDecay = 0, vcfSustain = 99, vcfRelease = 0;
            int vcfAmount = 0;           // signed

            int velToFilter = 0, keyToFilter = 0, velToLoudness = 0;
            int velToAttack = 0, velToRelease = 0;

            /*
             * WARP, bytes 12 to 14: a pitch bend at note-on that decays back to pitch. Byte 13
             * is the DEPTH and is signed; byte 12 is how far velocity scales it, with 0 meaning
             * always full; byte 14 is the time constant. See cal::warpRatio.
             */
            int warpVelocity = 0, warpDepth = 0, warpTime = 0;

            /*
             * Byte 19: which output the keygroup goes to. The panel value, which the byte
             * stores one lower - so ALL is -1, the default in 1617 of 1908 keygroups.
             *
             *     0 ALL,  1..8 MONO 1 to 8,  9 LEFT,  10 RIGHT
             */
            int outputPort = 0;

            int lfoDelay = 0, lfoRate = 0, lfoDepth = 0;
            int lfoAftertouchDepth = 0, lfoModwheelDepth = 0;

            int flags = 0;

            Zone zone1, zone2;

            bool constantPitch()    const { return (flags & 0x01) != 0; }
            bool lfoDesync()        const { return (flags & 0x04) != 0; }
            bool oneShot()          const { return (flags & 0x08) != 0; }

            /*
             * Bit 4: the ON/OFF beside Release on the velocity page, found by diffing a disk
             * saved either side of flipping it. It enables velToRelease - with it clear, every
             * note is released as though its velocity were 1, which is not the same as no
             * effect. Clear in all 1908 keygroups of the library.
             */
            bool velocityReleaseOn() const { return (flags & 0x10) != 0; }
            bool hasSecondZone()    const { return zone2.inUse(); }
        };

        // -------------------------------------------------------------------- loading

        /// Read an image from disk. False, with `error` set, if it is not one.
        bool loadFile (const std::string& path, std::string& error);

        /// The same from bytes already in hand - which is how a plugin will restore one
        /// out of a host's saved project.
        bool loadBytes (std::string name, std::vector<unsigned char> bytes, std::string& error);

        const std::string&        getName()    const { return source; }
        const std::vector<Entry>& getEntries() const { return entries; }

        /// True if this came out of an .hfe rather than a plain sector image.
        bool wasHfe() const { return fromHfe; }

        /*
         * The decoded sectors, as a plain image.
         *
         * For putting a disk somewhere it can be got back from - a host's saved project,
         * say. Always the sectors, never the .hfe it may have arrived in: decoding is
         * deterministic and one-way, so keeping the result means a reload does no MFM work
         * and cannot come out differently.
         */
        const std::vector<unsigned char>& getImage() const { return image; }

        /*
         * How the recovery went, for an .hfe. Both zero for a plain image.
         *
         * Worth showing rather than hiding: an archived floppy is thirty years old, and a
         * disk that reads with three bad sectors is a different thing from one that reads
         * cleanly - especially if it is about to be played into a recording.
         */
        int getBadCrcSectors()  const { return badCrcSectors; }
        int getMissingSectors() const { return missingSectors; }

        /// The entry of that name and type, or nullptr.
        const Entry* find (const std::string& name, char type) const;

        // -------------------------------------------------------------------- reading

        /// A file's bytes, following its chain through the allocation table.
        std::vector<unsigned char> readFile (const Entry& e) const;

        /// A sample as stored: signed 12-bit values, -2048..2047.
        std::vector<short> sampleWords12 (const Entry& e) const;

        /// A program's keygroup records, in order.
        std::vector<Keygroup> keygroups (const Entry& program) const;

        static int keygroupCount (const Entry& program);

        // ---------------------------------------------------------- ready to be played

        /*
         * Turn a programme into something the engine can play.
         *
         * The same job Instrument.SetProgram does in the C# editor, and the same rule at
         * the centre of it: the two zones of a keygroup are velocity ALTERNATIVES, not
         * layers. They split the range at the switch, and a switch of 128 leaves zone 1 the
         * whole of it, which is how the panel turns the second zone off.
         */
        PatchPtr buildPatch (const Entry& program) const;

    private:
        int  fat (int block) const;
        std::vector<int> chain (int start) const;
        void parseDirectory();
        void readSampleHeader (Entry& e) const;

        int  totalBlocks() const { return static_cast<int> (image.size()) / BlockSize; }

        unsigned int u32 (std::size_t at) const;
        unsigned int u16 (std::size_t at) const;

        static std::string cleanName (const unsigned char* b, std::size_t at);
        static Zone parseZone (const unsigned char* raw, int at);

        /// One sample, decoded and normalised, ready for a voice to read.
        SoundPtr soundFor (const Entry& sample) const;

        std::string                source;
        std::vector<unsigned char> image;
        std::vector<Entry>         entries;

        bool fromHfe = false;
        int  badCrcSectors = 0, missingSectors = 0;
    };
}
