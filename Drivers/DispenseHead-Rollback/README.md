# Dispense-head known-good driver package

Drop the known-good Prolific driver package here — the version that currently
gets reached today via "download latest, then Roll Back Driver" for the
dispense-head cable (COM1).

Required files (as extracted from Prolific's driver installer, or copied out of
`C:\Windows\System32\DriverStore\FileRepository\` from a machine already on the
correct version):
- `prolific.inf` (or whatever the package's actual .inf is named — update
  `DispenseHeadRollbackDriverInfPath` in Settings if the filename differs)
- accompanying `.cat`, `.sys`, `.dll` files referenced by the .inf

The app calls `pnputil /add-driver DispenseHead-Rollback\*.inf /install` against
this package during the rollback step, so all files the .inf references need to
sit alongside it in this folder.
