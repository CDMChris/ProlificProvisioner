# Dispense-head known-good driver package

The known-good Prolific driver package the dispense-head cable (COM1) gets
rolled back to, after the "download latest" step. Currently:

- `plser_1.inf` — DriverVer 06/07/2024, 5.2.7.0
- `plser_1.cat`, `plser_1.PNF`
- `plser64_1.sys`, `plser64_1.dll`

`AppConfig.DispenseHeadRollbackDriverInfPath` defaults to
`Drivers/DispenseHead-Rollback/plser_1.inf`; update it in Settings if this
package is ever replaced with one that has a different `.inf` filename.

The app calls `pnputil /add-driver Drivers\DispenseHead-Rollback\plser_1.inf
/install` against this package during the rollback step — all files above need
to stay together in this folder since the `.inf` references them by relative
path.
