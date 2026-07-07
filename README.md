# Prolific Cable Provisioning Assistant

Guided Windows desktop app for provisioning the two Prolific USB-serial cables
used on the assembly fixture:

- **COM1 — Dispense head**: install the latest driver, roll back to a known-good
  driver, force COM1, tell the worker how to label the cable.
- **COM2 — Printer**: install the fixed 2026 driver package, force COM2, tell the
  worker how to label the cable.

Handles the two-identical-devices problem (both cables report the same Prolific
hardware ID) by keying off physical USB port location, and gives every step a
timeout/retry budget that surfaces a **Cable Fault** state — Retry or Mark
Defective — instead of hanging when a cable is DOA.

## Project layout

```
src/ProlificProvisioner.Core/   # device/driver/COM-port logic — no WPF dependency
src/ProlificProvisioner.App/    # WPF UI (net8.0-windows), requireAdministrator
ProlificProvisioner.Core.Tests/ # xunit tests for Core (mocked hardware, no real devices)
Drivers/                        # bundled driver packages the app installs from
```

## Building

The whole solution — including the WPF app — compiles on macOS/Linux dev machines
via the cross-targeting flag:

```
dotnet build -p:EnableWindowsTargeting=true
```

This validates C# and XAML compile correctly, but does **not** prove the app runs
correctly — `pnputil`, the registry writes, and WMI device enumeration all only
function on Windows. On a normal Windows dev box you don't need the flag:

```
dotnet build
```

Run the Core unit tests (portable, no real hardware needed) on any platform:

```
dotnet test ProlificProvisioner.Core.Tests
```

## Running (Windows only)

```
dotnet run --project src/ProlificProvisioner.App
```

The app requests elevation on launch (`app.manifest` sets
`requireAdministrator`) — driver install/removal and the COM-port registry
writes need admin rights.

## First-time setup on a new fixture

1. Drop the known-good dispense-head driver package into
   `Drivers/DispenseHead-Rollback/` and the 2026 printer driver into
   `Drivers/Printer-2026/` (see the README in each folder).
2. Launch the app, open **Settings → Learn Fixture Ports**, and plug a cable into
   each physical fixture slot in turn, tagging each as Dispense Head or Printer.
   This mapping is what lets the app tell the two identical-ID cables apart.
3. From then on, plugging a cable into either slot auto-runs its provisioning
   sequence.

## Verification checklist (run on the real Windows assembly PC)

- [ ] Plug a known-good cable into the dispense-head slot → app installs latest
      driver, rolls back to the bundled known-good driver, assigns COM1, shows
      the label text.
- [ ] Plug a known-good cable into the printer slot → app installs the bundled
      2026 driver, assigns COM2, shows the label text.
- [ ] Plug both cables in simultaneously → each still resolves to the correct
      role/COM port (proves the location-path mapping, not hardware ID, is
      driving role resolution).
- [ ] Unplug a cable mid-sequence → its card resets to "Waiting for cable"
      rather than getting stuck.
- [ ] Simulate a bad cable (e.g. a cable with a known-broken data line, or pull
      it right after detection) → after the configured retry count, the card
      shows **Cable Fault** with Retry / Mark Defective, and Mark Defective logs
      the event to `provisioning-log.csv` next to the executable.
- [ ] Confirm `provisioning-log.csv` has one row per attempt with timestamps,
      matching what happened on screen.
