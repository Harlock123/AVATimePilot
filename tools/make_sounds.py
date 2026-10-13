#!/usr/bin/env python3
"""Synthesises AVATimePilot's sound effects and tunes into ../src/AVATimePilot/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVATimePilot", "Sounds")
RNG = random.Random(1985)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def main():
    os.makedirs(OUT, exist_ok=True)
    write("Shoot.wav", tone(0.07, 1800, 700, "pulse", release=1.3), 0.22)
    write("Hit.wav", mix(tone(0.22, 600, 70, "noise", release=0.8), tone(0.16, 300, 80, "square", release=1.0)), 0.4)
    write("Explode.wav", mix(tone(1.3, 400, 30, "noise", release=0.5), tone(1.1, 120, 30, "square", release=0.6, gain=0.6)), 0.6)
    write("Die.wav", cat(mix(tone(0.5, 500, 40, "noise", release=0.5), tone(0.5, 900, 80, "saw", release=0.5)),
                         *[tone(0.16, hz(n), kind="square", release=0.6) for n in ["E5", "C5", "A4", "E4"]]), 0.55)
    write("Missile.wav", tone(0.45, 300, 1400, "saw", release=0.4, vibrato=0.05, vib_rate=25), 0.3)
    write("Parachute.wav", cat(*[tone(0.07, hz(n), kind="tri", release=0.4) for n in ["C6", "E6", "G6", "C7", "G6", "C7"]]), 0.42)
    write("BossAlarm.wav", cat(*[tone(0.22, f0, f1, "square", release=0.2) for f0, f1 in [(600, 900), (600, 900), (600, 900)]]), 0.38)
    write("BossHit.wav", mix(tone(0.08, 200, 120, "square", release=1.0), tone(0.06, 1200, 300, "noise", release=1.0)), 0.32)
    write("ExtraLife.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["C6", "G5", "C6", "G5", "C6", "E6", "G6"]]), 0.4)
    write("Formation.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.4) for n in ["G5", "B5", "D6", "G6", "D6", "G6"]]), 0.38)
    # The time warp: everything winds up and up, then a chord as the new era opens (new).
    write("Warp.wav", cat(mix(tone(2.2, 120, 3000, "saw", release=0.1, vibrato=0.08, vib_rate=12),
                              tone(2.2, 3000, 100, "tri", release=0.2, gain=0.6),
                              tone(2.2, 2000, 200, "noise", release=0.3, gain=0.3)),
                          mix(tone(1.0, hz("C5"), kind="tri", release=0.5), tone(1.0, hz("E5"), kind="tri", release=0.5),
                              tone(1.0, hz("G5"), kind="tri", release=0.5), tone(1.0, hz("C6"), kind="tri", release=0.5))), 0.5)

    # Flying: a quick, bright figure in C over a bass that walks up and back, with the engine's hum
    # under it (new). Loops.
    lead = [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("E5", 0.5), ("A5", 0.5), ("G5", 0.5), ("E5", 1),
            ("F5", 0.5), ("A5", 0.5), ("C6", 0.5), ("A5", 0.5), ("G5", 0.5), ("E5", 0.5), ("D5", 1),
            ("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("B5", 0.5), ("G5", 0.5), ("A5", 0.5), ("F5", 0.5),
            ("E5", 0.5), ("D5", 0.5), ("G4", 0.5), ("B4", 0.5), ("C5", 2)]
    bass = [(n, 1) for n in ["C3", "G2", "A2", "E2", "F2", "C3", "G2", "G2", "C3", "E3", "F3", "D3", "G2", "G2", "C3", "C3"]]
    song = tune(150, lead, bass)
    engine = tone(len(song) / RATE, 55, kind="saw", attack=0.001, release=0.0, vibrato=0.02, vib_rate=7, gain=0.18)
    write("Music.wav", mix(song, engine), 0.42)

    write("TitleTune.wav", tune(132,
        [("G4", 0.5), ("C5", 0.5), ("E5", 0.5), ("G5", 1.5), ("E5", 0.5), ("G5", 0.5), ("C6", 2),
         ("B5", 0.5), ("A5", 0.5), ("G5", 0.5), ("F5", 0.5), ("E5", 0.5), ("D5", 0.5), ("E5", 2),
         ("F5", 0.5), ("A5", 0.5), ("C6", 0.5), ("A5", 1.5), ("G5", 0.5), ("E5", 0.5), ("C5", 2),
         ("D5", 0.5), ("E5", 0.5), ("F5", 0.5), ("D5", 0.5), ("G5", 1), ("B4", 1), ("C5", 2)],
        [(n, 2) for n in ["C3", "C3", "G2", "C3", "F2", "F2", "G2", "C3"]]), 0.55)
    write("StartTune.wav", tune(160,
        [("C5", 0.5), ("C5", 0.25), ("C5", 0.25), ("G5", 1), ("E5", 0.5), ("G5", 0.5), ("C6", 2)],
        [("C3", 1), ("G2", 1), ("C3", 2), ("C3", 1)]), 0.55)
    write("GameOver.wav", tune(84,
        [("G5", 1), ("E5", 1), ("C5", 1), ("D5", 0.5), ("B4", 0.5), ("C5", 3)],
        [("C3", 2), ("G2", 2), ("C3", 3)], kind="tri"), 0.55)


if __name__ == "__main__":
    main()
