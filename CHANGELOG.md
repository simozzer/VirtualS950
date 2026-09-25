# Changelog

What changed between releases, for the notes that go with a tag.

Every number here came off a recording of a real S950 rather than out of a manual, so where
a change is a measurement it says what was measured and what it replaced. Where it is an
assumption it says that too — those are the ones another afternoon with a recorder settles.

## Unreleased

### The velocity switch was out by one, in all three engines

A keygroup holds two samples and byte 2 says where one hands over to the other. Every engine
read that byte as the *first* velocity of zone 2. It is the *last* velocity of zone 1.

Measured on the hardware, with a sine in zone 1 and white noise in zone 2 so that which one
answered needed no interpretation:

| switch | zone 1 through | zone 2 from |
|---|---|---|
| 1 | 1 | 2 |
| 64 | 64 | 65 |
| 90 | 90 | 91 |
| 127 | 127 | never |

The last line is what makes it certain rather than merely consistent. At a switch of 127 the
hard sample cannot be reached at all, which only follows if zone 2 begins at 128 — and that
is why the panel's range runs to 128 and why 128 means the switch is off. What used to be a
special case in the code saying so has gone: with zone 2 starting at `split + 1`, a split of
127 or 128 leaves it an empty range and the engines decline it on their own.

It is a switch and not a crossfade. Every clip read either 0.639 or 0.0001 of its energy at
the tone's frequency, with nothing in between anywhere near a boundary.

The practical effect is one velocity step at each switch point, which matters most where a
programme puts the switch low or high — and it is exactly the kind of error that survives
three implementations agreeing with each other.

## v0.3.0 — 2026-09-25

### The player's controls — new

Fifteen controls that sit on top of whatever programme is loaded, where before there was only
gain. Every one is an *offset* from what the disk says, reads zero until it is turned, and
double-clicks back to zero — zero meaning "play what is on the floppy". A programme carries
its own cutoff and envelope per keygroup, often quite different ones across the keyboard, and
an absolute control would flatten all of that the moment it was touched.

All fifteen are automatable by the host and all fifteen answer a MIDI controller:

| group | controls | CC | range |
|---|---|---|---|
| Sample | Filter | 74 | ±99 |
| VCF | Amnt | 70 | ±50 |
| VCA envelope | Attack, Decay, Sustain, Release | 73, 75, 79, 72 | ±99 |
| VCF envelope | Attack, Decay, Sustain, Release | 102, 103, 104, 105 | ±99 |
| LFO | Rate, Depth, Delay | 76, 77, 78 | 0..99 |
| Velocity | Freq, Loudness | 109, 112 | 0..99 |

72–79 are the General MIDI sound-controller numbers, so a keyboard with knobs already
labelled *cutoff*, *attack* or *vibrato rate* reaches the right ones unmapped. 102–105 and
109/112 are undefined numbers taken for the filter envelope and for velocity, which GM has no
assignments for.

The two envelopes are dragged as shapes rather than set as eight knobs, and the graph draws
the result for one representative keygroup — the programme's own values with the trim added —
so it shows what will be heard rather than an abstract curve.

Three behaviours worth knowing:

- **The LFO and velocity knobs only add**, 0..99, where the filter and envelope trims go both
  ways. Nearly every programme leaves the LFO switched off, so a symmetric knob spent its
  whole lower half asking for less than nothing.
- **Velocity → Loudness reaches the next note played**, not one already sounding: how hard a
  key was struck is settled when it goes down. Velocity → Freq does reach a sounding note.
- **The VCA attack steps rather than slides**, because on the hardware it is a counter. See
  below.

Not included: velocity to attack and to release. The keygroup carries both (bytes 9 and 10),
no engine models either, and no keygroup in the six-disk library sets either — 0 in all 181 —
so there is nothing to calibrate against and nothing that would play differently. Controls for
those would be controls over invented behaviour.

### The filter envelope has a release — new

It never had one. The filter fell back to the keygroup's own cutoff the instant a key came up,
whatever the release byte said.

### Measured corrections to the engine

**The envelope time curve was one measured point stretched over the whole range.** A VCA decay
at stored 80, and an assumed 10000:1 span that nothing had ever checked. The span was the part
that was wrong — it is nearer 1000:1 — so the old curve ran at about half the machine's speed
below stored 70 and nearly twice it above 85, and was right only at 80.

| stored | 50 | 55 | 60 | 65 | 70 | 80 | 85 | 90 | 95 |
|---|---|---|---|---|---|---|---|---|---|
| measured | 0.357 | 0.418 | 0.722 | 0.881 | 1.404 | 2.814 | 4.037 | 4.117 | 8.095 |
| was | 0.176 | 0.280 | 0.446 | 0.711 | 1.131 | 2.868 | 4.567 | 7.272 | 11.580 |

Attack, decay and release agree on this one curve to 1.07x where they overlap.

**The release is a rate, not a duration.** The release byte sets how fast the envelope falls,
so a release from half depth is over in half the time. The filter did the opposite — fall to
zero over the release time however far there was to go — while the amplitude in the same voice
did the right thing. Nothing had caught it because every release ever measured started from a
sustain of 99 and fell the whole depth, the one case where the two rules agree.

**The VCA attack is a counter.** Every one of thirteen measured settings is 5.4/n seconds for
a whole number n, to within 0.7%. That explains what no curve could: stored 70 and 75 return
identical attacks to four digits, as do 80 and 85, and 90 through 99 all saturate at 2.70 s
where the old model asked for 10.74. `AttackScale` is gone — it was a single multiplier on the
shared curve and no value of it could be right.

**Attack 0 is a hard gate.** Not measured — nothing reaches below stored 30 — but it is how
envelope generators are built, and a 30-unit extrapolation had put it at 40 ms, which is an
audible softening on every percussive sample.

**Three constants from an earlier run**: the envelope's full-amount depth 7.6 → 8.5 octaves,
the cutoff at stored 50 from 1878 → 2210 Hz, and a key-tracking pivot of note 62, which is new
— tracking does not pivot at middle C, and assuming it did quietly offset every cutoff read
from a keygroup with tracking on.

### The editor and the host

- Programmes are offered to the host as the plugin's programs, so a DAW's own program selector
  changes sound without opening the plugin window.
- A saved song carries the whole disk image, gzipped, rather than a path to it.
- Samples can be exported as WAV, and files copied between open images.
- A velocity fader beside the on-screen keyboard. Every click used to play at a fixed velocity
  of 100, which on the measured curve sits about 1.2 octaves above the pivot — so on any
  programme with velocity-to-filter the filter appeared to do nothing.
- A bundled drum kit, drawn rather than recorded.

### Known gaps

Measurement rather than code, and listed in `Plugin/README.md` in full:

1. `EnvOctaves` is 8.5 and two independent readings suggest nearer 7.8.
2. `SustainDb` says a decay bottoms out 39.6 dB down; the hardware falls at least 77, and
   "at least" is as far as the recording's noise floor allows.
3. Velocity to attack and to release are read off the disk and dropped.
4. Nothing measures the VCA attack below stored 30.
5. The filter attack may quantise like the VCA's; it was measured too coarsely to tell.

## v0.2.0 — 2026-09-21

The first release with the plugin in it.

## v0.1.0

The editor.
