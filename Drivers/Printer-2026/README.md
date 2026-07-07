# Printer driver package (2026 release)

The current Prolific driver package used for the printer cable (COM2). Currently:

- `plser_1.inf` — DriverVer 01/22/2026, 5.2.11.0
- `plser_1.cat`, `plser_1.PNF`
- `plser64_1.sys`, `plser64_1.dll`

`AppConfig.PrinterLatestDriverInfPath` defaults to
`Drivers/Printer-2026/plser_1.inf`; update it in Settings if this package is
ever replaced with one that has a different `.inf` filename.

Unlike the dispense-head port, the printer port always uses this fixed bundled
package directly — no rollback step. When Prolific ships a newer driver you
want to standardize on, replace the files in this folder (and bump the version
note above) rather than relying on Windows Update at provisioning time.
