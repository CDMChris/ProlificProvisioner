# Printer driver package (2026 release)

Drop the current Prolific driver package used for the printer cable (COM2) here —
the "latest, 2026" driver referenced in the provisioning spec.

Required files:
- `prolific.inf` (or whatever the package's actual .inf is named — update
  `PrinterLatestDriverInfPath` in Settings if the filename differs)
- accompanying `.cat`, `.sys`, `.dll` files referenced by the .inf

Unlike the dispense-head port, the printer port always uses this fixed bundled
package directly — no rollback step. When Prolific ships a newer driver you want
to standardize on, replace the files in this folder (bump a version note here)
rather than relying on Windows Update at provisioning time.

**Currently only `plser64_2026.sys` is here — still missing the `.inf` (and
usually a `.cat`).** Same issue as the dispense-head folder: `pnputil` needs the
`.inf` to stage anything. Pull the matching `.inf`/`.cat` for this 2026 release
from Prolific's driver download or `C:\Windows\System32\DriverStore\FileRepository\`
and drop them in here next to the `.sys`.
