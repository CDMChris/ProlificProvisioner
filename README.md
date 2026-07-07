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

The whole solution — including the WPF app — compiles with a plain:

```
dotnet build
```

on Windows, macOS, or Linux (the App project auto-enables cross-targeting when
not on Windows, so no extra flags needed). On non-Windows this validates that
the C# and XAML compile correctly, but does **not** prove the app runs
correctly — the SetupAPI driver binding, registry writes, and WMI device
enumeration all only function when actually run on Windows.

### How driver install/rollback actually works

Windows has no supported API for the literal Device Manager "Roll Back Driver"
button — it's an internal, undocumented action whose outcome depends on
whatever happened to be installed before, which isn't reproducible on demand.
Instead, both fixture slots use `IDriverBinder`
([`src/ProlificProvisioner.Core/Drivers/NativeDriverBinder.cs`](src/ProlificProvisioner.Core/Drivers/NativeDriverBinder.cs))
to force-install a specific bundled `.inf` onto one exact device instance —
the same SetupAPI mechanism (`SetupDiInstallDevice`) behind Device Manager's
per-device "Update Driver → Let me pick from a list → select a specific
model → Next" flow. The dispense-head slot force-installs the "latest"
package first, then force-installs the known-good package — landing on the
same known-good end state every time, deterministically, rather than
depending on driver history.

This is deliberately **not** `pnputil`, `devcon update`, or
`UpdateDriverForPlugAndPlayDevicesW` — all three force a driver by hardware
ID, which would hit **every** currently-connected device sharing that ID at
once. Since both fixture cables report the same Prolific hardware ID, that
would cross-contaminate the dispense-head and printer driver versions
whenever both are plugged in simultaneously. `NativeDriverBinder` scopes
every call to one exact device instance ID instead.

COM port assignment uses the same per-device scoping for the "make Windows
re-read the new port name" step: instead of `pnputil /restart-device`, it
disables then re-enables the specific device instance (`DIF_PROPERTYCHANGE`),
matching Device Manager's "Disable device" / "Enable device" actions — a full
devnode teardown/reload is more reliable at forcing a re-read than a softer
restart.

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
- [ ] Plug a cable in *before* its fixture port has been Learned, then run
      Learn Ports and tag it without unplugging it — the dashboard card should
      start provisioning within ~1 second of tagging, with no app restart
      needed.
- [ ] If the app ever appears to freeze/stop updating, check
      `crash-log.txt` next to the executable — unhandled exceptions on any
      thread get logged there instead of silently killing the app.
