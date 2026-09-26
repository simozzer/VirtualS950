# VirtualS950 — the plugin

The S950 engine as a VST3, so a programme off a real disk can be played from a DAW.

## Using it

The installer puts the VST3 in the folder hosts scan and the standalone player in the
program group. If your DAW has already scanned its plugin folders, tell it to rescan;
**VirtualS950** then appears under instruments.

**Load a disk.** Press **Load disk…** and pick an `.hfe` or `.img`. The browser opens
*inside* the plugin window rather than as a system dialog — a native chooser goes to the
primary display, and a plugin opened on a second monitor would put its file browser where
nobody is looking, which from the DAW is indistinguishable from a button that does nothing.
It starts beside the disk already open, or the folder a disk was last chosen from, or the
sound library the installer left on the machine, whichever it finds first.

**Change programme** either in the plugin's own box or in the host's program selector — the
disk's programmes are offered to the host as the plugin's programs, so in Ableton Live the
Program chooser in the device title bar changes sound without opening the window.

**A saved song carries the disk.** The whole 800K image goes into the project, gzipped, not
a path to it: a few hundred kilobytes beside the audio a session already holds, and a set
that can never lose the sound it was made with. Move the image, rename it, or open the song
on another machine — it still plays. The programme comes back by name first and by position
second, so a disk edited and reordered since still returns what you meant.

**The player's controls.** Sixteen parameters, all automatable by the host and all reachable
from a MIDI controller: gain, and fifteen trims that move every keygroup of the loaded
programme together.

A trim is an *offset* from what the disk says, not a setting. It reads zero until you turn it
and double-clicks back to zero, and zero means "play what is on the floppy". That matters
because a programme carries its own cutoff and envelope per keygroup — often quite different
ones across the keyboard — and an absolute control would flatten all of that the moment it was
touched. An offset keeps the shape its author gave it and moves the whole of it, which is what
"brighter" means on an instrument like this.

| group | controls | CC | range |
|---|---|---|---|
| Sample | Filter | 74 | ±99 |
| VCF | Amnt | 70 | ±50 |
| VCA envelope | Attack, Decay, Sustain, Release | 73, 75, 79, 72 | ±99 |
| VCF envelope | Attack, Decay, Sustain, Release | 102, 103, 104, 105 | ±99 |
| LFO | Rate, Depth, Delay | 76, 77, 78 | 0..99 |
| Velocity | Freq, Loudness | 109, 112 | 0..99 |

72–79 are the General MIDI sound controllers, so a keyboard with knobs labelled *cutoff* and
*attack* reaches the right ones with no mapping — including 76, 77 and 78 for vibrato rate,
depth and delay, which is what this machine's LFO is. 102–105 and 109/112 are undefined
numbers taken for the filter envelope and for velocity, which GM has no assignments for.

The two envelopes are dragged as shapes rather than set as eight knobs: the corners are the
stages, and the graph shows the result for one representative keygroup — the programme's own
values with the trim added — so it is honest about what you will actually hear.

Three behaviours worth knowing before they surprise you:

- **The LFO and velocity knobs only add.** The filter and envelope trims go both ways because
  a programme always has an envelope and always has a cutoff. Nearly every programme leaves
  the LFO switched off, so a symmetric knob there would spend its whole lower half asking for
  less than nothing and clamping at zero.
- **Velocity → Loudness reaches the next note you play**, not one already sounding. It decides
  how much softer a soft note is, which is a question about the strike, and the strike is over.
  Velocity → Freq does reach a sounding note, because a filter control you cannot play with is
  not a control.
- **The VCA attack steps rather than slides.** On the hardware it is a counter — 5.4/n seconds
  for whole n — so stored 70 and 75 give the same attack to four digits, as do 80 and 85, and
  everything from 90 up saturates at 2.70 s. The knob is faithful to that, which does feel odd
  under the mouse the first time.

To change the programme itself rather than trim it, edit the image in the Studio, save it, and
load the saved image here.

Eight voices, as the machine had. A ninth note takes a voice that is already releasing
before it takes one still held. The voice count in the window is there because it answers
the first question anyone asks of a plugin making no noise — is it getting the notes? —
without a debugger.

`docs/tutorial.html` in the installed folder has the same ground with the editor's half
as well.

## Where this has got to

`Source/S950` is the engine, ported from `AkaiS950Engine` and depending on nothing but the
C++ standard library — no JUCE, no SDK, no audio device. That is deliberate: it is the half
worth being sure of, and keeping it free of everything else means it can be compiled and
checked on its own, long before a plugin will load.

```
Source/S950/Cal.h        the measured constants, and the mappings from panel bytes
Source/S950/Filter.h     6th-order Butterworth, three biquads
Source/S950/Patch.h      Sound, KeygroupPatch, Patch — what a voice needs
Source/S950/Disk.h/cpp   reading an .hfe or .img into a Patch
Source/S950/Voice.h/cpp  one sounding note
Source/S950/Engine.h/cpp eight voices, the event ring, the patch hand-off
Tests/ConformanceCheck.cpp
Tests/Reference.h        GENERATED — what the C# computes, to be held to
```

**It compiles, and it agrees with the C# exactly.** For one commit it was a transcription
nobody had run, because the machine it was written on had no C++ compiler. The first build
once Visual Studio arrived was clean at `/W4`, and the conformance check passes 289 of 289 —
with the worst relative difference across the filter's 65 cutoff points at exactly 0.

## What it needs

- **Visual Studio Community 2026** with "Desktop development with C++" — what this was built
  with, MSVC 14.51. Note that `cl.exe` is never on the PATH: the installer leaves it off on
  purpose, because the compiler needs INCLUDE, LIB and PATH set for one target architecture.
  `build.ps1` finds `vcvars64.bat` through vswhere and sets them; the Start menu's "Developer
  PowerShell for VS 2026" does the same thing by hand.
- **JUCE** — `git clone https://github.com/juce-framework/JUCE`. No installer, and it carries
  the VST3 SDK headers, so there is nothing to fetch from Steinberg. Drive it with JUCE's
  CMake support rather than a Projucer-generated solution: the Projucer emits project files
  for the Visual Studio versions it knows by name, and CMake does not care which one is
  installed.

JUCE 9 is **AGPLv3** unless you buy a licence — not GPL3, which is what JUCE 6 and 7 were.
`VirtualS950` is AGPLv3 to match, so there is no gap between what this repository says and
what the JUCE parts oblige. A closed-source plugin would need a commercial JUCE licence.

## Building the plugin

```
cd Plugin
cmake -S . -B build
cmake --build build --config Release --parallel
```

CMake ships inside Visual Studio, so there is nothing else to install — `build.ps1` finds it
the same way it finds the compiler. That produces three things:

- a **VST3**, installed to `%LOCALAPPDATA%\Programs\Common\VST3`. JUCE would rather put it in
  `C:\Program Files\Common Files\VST3`, but that cannot be written to — or created — without
  elevation, and building as administrator to test an audio plugin is the wrong trade. The
  per-user folder is the other location the VST3 specification names. In Ableton, add it once
  under Preferences → Plug-Ins → VST3 Plug-In Custom Folder.
- a **standalone** at `build/VirtualS950_artefacts/Release/Standalone/VirtualS950.exe`, which
  opens without a DAW. That is what makes "is the plugin broken, or is the host unhappy with
  it" answerable in one step rather than two.
- the **conformance check**, at `build/Release/ConformanceCheck.exe`.

## Checking the engine on its own

```
cd Plugin
.\build.ps1
```

It needs no JUCE and no audio device. It puts the same inputs through the port that
`ReferenceDump` put through the C#, and insists on the same answers to nine decimal places —
the filter's cutoff at 65 points, the envelope times at 35, the VCA attack at all 100 of its
settings, the LFO's rate and delay fade, every measured constant, plus the filter's DC gain
and stopband and the engine driven end to end through its event ring.

The attack gets all hundred rather than a ladder because it is a counter, and the interesting
thing about a counter is *where it steps*: a port that interpolated smoothly between the same
measured points would match every third value and be wrong in between. The check also holds
the property rather than only the numbers — whole steps, never going backwards, saturating at
5.4/2 — and does the same for the player's controls, which are tested by what they do to
rendered audio rather than by reading a variable back. A control that moved a number without
moving the sound would pass any test that asked the number.

Regenerate `Reference.h` after any change to `Cal`, `Filter` or the envelopes on the C# side:

```
.\test.ps1                                     # from the repo root, to be sure the C# is sound
csc /target:exe /main:ReferenceDump /out:%TEMP%\RefDump.exe AkaiS950Tests\ReferenceDump.cs AkaiS950Engine\*.cs
%TEMP%\RefDump.exe Plugin\Tests\Reference.h
```

## Why the port looks like the C#

Almost line for line, on purpose. Three implementations of this instrument now exist — the
web's `audio.js`, `AkaiS950Engine`, and this — and they are meant to agree to the digit,
because every number in them came off a recording of a real machine rather than out of a
manual. Keeping the shape identical is what makes a disagreement easy to find.

Two places where it could not stay identical, both in `Engine`:

- **The patch hand-off.** The C# assigned a reference and let the collector decide when the
  old programme could go. There is no collector here, and both obvious replacements free
  memory on the audio thread. The patch is moved between threads through a two-slot
  exchange instead; `Engine.cpp` says why at length. Call `collectRetiredPatch()` from the
  message thread, or the old patch is never released.
- **Sound ownership.** `Sound` is held by `shared_ptr`, which the C# did not need. A voice
  reads a sample buffer on the audio thread while the message thread may be replacing the
  programme, and shared ownership is what stops the buffer being freed underneath it.

## Still to do

The three things that were listed here — reading disks, saving the image into the project,
and an editor worth looking at — are all done. What is left is measurement, not code:

1. **`ENV_TIME` is about 20% slow around stored 45.** Run 9 measured the release span as a
   constant 41.0 dB (spread 0.7) at twelve settings from stored 20 to 95 — but the four clips
   landing near stored 45 imply 49.3. Same place, same direction and same size as the
   velocity-release fit's worst misses, so it is the curve rather than either rule. The table
   has no measured point between 0 and 50, which is where this falls; one run with a few
   settings in that gap would close it.
2. **`EnvOctaves` is probably 8.5 when it should be nearer 7.8.** Two independent readings
   from the fifth calibration run say so: the static corners of a negative-amount clip, and
   the travel of a full-depth release. Neither was the run's purpose, so neither is clean
   enough to change a constant on.
2. **`SustainDb` says a decay bottoms out 39.6 dB down, and the hardware falls at least 77.**
   "At least" is as far as it goes: −77 dB is where the recording's noise floor sat. The
   measurement at sustain 50 is unaffected and still right.
3. **Both velocity sensitivities are modelled now**, and they do not share a shape. Byte 9
   shortens the attack with no pivot at all — velocity 1 leaves the byte where it is. Byte 10
   turns the release about velocity 64, so a soft strike lengthens it where a hard one shortens
   it, or the reverse if the depth is negative. Both are in the changelog with their numbers.

   The residual uncertainty is in the envelope table rather than in either rule: the release
   fit is within about 9% across eighteen clips, and its worst misses cluster around effective
   byte 45, all on the same side, which is the signature of a table that is slightly off there
   rather than a rule that is wrong.

   An earlier version of this list said neither was worth the trouble because no keygroup in
   the bundled library set them. That library is generated by this repository, so it only ever
   said what this repository writes; the 101 real disks set byte 9 in 164 keygroups.
4. **The VCA attack below stored 30 is measured at two points and interpolated elsewhere.**
   Stored 8 and 20, reached sideways through the velocity rule rather than directly, so they
   inherit whatever that rule gets wrong. That attack 0 is a hard gate is still how envelope
   generators are built rather than something read off a machine — though extrapolating those
   two points downwards puts stored 0 near 7.6 ms, which is the first evidence to bear on it.
5. **The filter attack may quantise like the VCA's** — 5.4/n for whole n — but it was
   measured to about 5%, which is far too coarse to see 0.7% steps, so it keeps the shared
   envelope curve. Not because it is smooth; because nobody has looked.
6. **Warp's depth constant is a choice within the measurement.** 6.25 cents per unit of byte 13
   is used because it is a sixteenth of a semitone exactly; the fit gives 6.21 and cannot
   separate 6.0 from 6.5 — rms is 9.3%, 7.3% and 7.2% across those three. The shape, the
   velocity law and the time curve are all measured properly; only this one scale is rounded.
7. **LFO depth from aftertouch (byte 21) is read and dropped**, and the plugin has no
   channel-pressure handling at all. It is 0 in every one of the 1908 real keygroups, so
   nothing on any disk plays wrongly — it would be for your own patches.
8. **The eight individual outputs are played centred, which is a guess.** Byte 19 sends a
   keygroup to ALL, to one of MONO 1–8, or hard LEFT or RIGHT. LEFT and RIGHT are modelled;
   MONO 1–8 are centred because nobody has checked what the machine's main stereo pair does
   with a voice routed to an individual socket. If it drops out of the main mix — as it does
   on many samplers of that era — then 253 library keygroups should be silent on the stereo
   pair rather than centred. One minute with the hardware settles it: set a keygroup to
   MONO 1, play it, listen to the main outs.

## Sample-accurate events

`noteOn`, `noteOff`, `modwheel` and `allNotesOff` each take an `at` — where in the next block
the event belongs, in samples. `render` fills the block in stretches between events rather
than applying them all at the top, so a note lands on the sample the host asked for.

This is the one place the port deliberately does *more* than the C#, and it is not a
refinement. Applying everything at the block boundary still sounds like a working
instrument — just one that quantises every note it is sent to the buffer size. At 512
samples that is 11 ms, which nobody hears as a fault; they hear a drum machine that does not
quite swing. The C# has no use for it, because a piano keyboard and a MIDI port have nothing
finer to offer, and `at` defaults to 0, which is exactly that behaviour.
