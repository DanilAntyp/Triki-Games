# Triki Arcade

A Windows (WPF / .NET 8) arcade with three games that can be driven by a **Triki** BLE motion
controller: **Triki Pong**, **Endless Traffic Racer**, and **Triki Space** (a 2D space shooter).
All three share one controller connection, and every game has a full **keyboard fallback** — so
you can play without any hardware.

## Run it (for players)

**The easy way — no install needed:**

1. Download **`TrikiPong.exe`** (a single self-contained file, ~75 MB — it bundles .NET and WPF,
   so nothing else has to be installed). Grab it from the repo's
   [**Releases**](../../releases) page.
2. Double-click it.
3. On first launch Windows SmartScreen may show *"Windows protected your PC"* because the file
   isn't code-signed. Click **More info → Run anyway**. This is normal for hobby apps.

Requirements: **Windows 10/11, 64-bit**. The Triki controller connects over **Bluetooth LE**
(Windows 10 build 17763+). No controller? Every game works on the keyboard.

## Build & run (for developers)

```
dotnet build TrikiPong.csproj
dotnet run --project TrikiPong.csproj
```

The csproj sets `<RollForward>LatestMajor</RollForward>` so the .NET 8 build launches on a
newer installed runtime.

### Building the standalone exe yourself

```
dotnet publish TrikiPong.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o dist
```

The result is `dist/TrikiPong.exe`. Upload that one file to a GitHub Release so friends can
download and run it.

## Connecting Triki

1. Wake the Triki controller (press its button).
2. On the connect screen, click **Connect Controller** (BLE scan + auto-reconnect), or
   **Play without controller** to use the keyboard.
3. Pick a game from the menu. **Settings** adjusts global controller sensitivity.

Triki streams an IMU only: **gyro** (deg/s) and **accelerometer** (g). It does *not* report an
absolute position in the room, and it currently exposes **no button field** in its BLE frames.

---

## Triki Space

A bottom-lane survival shooter. Your ship is pinned to the bottom of the screen and only slides
**left/right**; enemies and asteroids dive from the top and shoot back at you. The gun fires
**automatically, straight up**. A **how-to-play screen** appears first — press any key or click
**Start** to begin.

| Action | Triki | Keyboard |
| --- | --- | --- |
| Move left/right | **Roll** the controller sideways (Z axis) — a velocity, like the Pong paddle | A / D or ← / → |
| Fire | Automatic, straight up (no overheat) | auto |
| Boost (quick dodge) | **Shake** the controller | Left Shift |
| Super shot | One sharp **impact** (when the SUPER meter is full) | Enter / F |
| Pause | — | Esc |
| Dev panel | — | F1 |
| BLE raw monitor | — | F2 |

Movement is a **rate control**: how fast you roll the controller sets how fast the ship slides,
reusing the exact gyro-Z scheme from Triki Pong — so there is nothing to calibrate.

### Gestures: shake vs. impact

Both are detected from **linear** acceleration (gravity subtracted). A single detector arbitrates
so the same motion never triggers both:

- A strong, sharp first impulse becomes an **impact candidate**.
- If more impulses with direction reversals arrive inside the window, it becomes a **shake** (boost).
- Otherwise, after a short confirmation delay, it fires as an **impact** (super shot).

All thresholds are tunable live in the dev panel and start from the values in
`TrikiSpace/GestureSettings.cs`. They are defaults and **will need tuning on real hardware**.

### Dev panel (F1)

Live readouts (packet rate, accel/gyro, gravity, linear accel, jerk, aim, gesture state, impulse
counts, cooldowns, meters, queue size) plus sliders for every steering/shake/impact parameter and
buttons: Calibrate, Reset aim, Start/Stop/Save/Load recording, Play/Stop playback, Sim shake, Sim
impact, Save/Reset settings. Settings persist to `%LocalAppData%/TrikiPong/space_settings.json`.

### IMU recording & playback

With Triki connected, **Start rec** captures the live IMU stream; **Save rec** writes CSV
(`timestampMs,accelX,accelY,accelZ,gyroX,gyroY,gyroZ,button`). **Load rec** + **Play** replay a
capture through the exact same gesture pipeline — so you can tune thresholds against a real motion
without holding the controller. Use this to turn the default thresholds into good ones.

### Investigating the Triki button

Because the BLE frame carries no button field today, firing is automatic. Press **F2** for a live
raw-packet monitor showing each notification's length, hex, and which byte index changed. Connect
Triki, press/release its button, and watch for a changing byte or a different-length packet. If one
appears, we can parse it and gate firing on the real button with no gameplay redesign.

## Tests

```
dotnet test Tests/TrikiSpace.Tests.csproj
```

20 xUnit tests cover the gravity filter, the shake/impact recognizer (no false positives from
noise or slow tilt; single impact; single shake; mutual exclusion; cooldowns; re-fire), and the
recording CSV round-trip + playback. They feed synthetic samples with controlled timestamps — no
Bluetooth and no `Thread.Sleep`.

## Known limitations

- Triki reports motion only, never an absolute room position.
- No real fire button yet (see the raw monitor above); firing is automatic.
- Gesture thresholds are unvalidated defaults until tuned on hardware (use record/playback).

## Deferred / next ideas

More waves/enemy variety, on-screen dev-panel toggles for invert axes, and wiring a real button
into firing once its BLE encoding is found.
