# Verification assets

This folder holds files that tests and manual checks use. It is not documentation.

| Path | Used by |
|---|---|
| `application-ports/ReceiverChecks.py` | `Abox.Infrastructure.Tests` (copied into its output) |
| `user-secrets/metadata/*.policy.kit.json` | `Abox.IntegrationTests` (copied into `Fixtures/Verification/`) |
| `https/RunReceiverChecks.ps1`, `https/ReceiverChecks.py` | manual check of the HTTPS receiver in a disposable Linux container |
| `user-secrets/` probes, kits and scripts | the spike that chose the user-secrets transport; no test uses them |

How to run the tests is in [development.md](../development.md#running-the-tests).
