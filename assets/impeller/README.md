- **Commit:** `cda25ea220c923a601dbc33ed4e9dd4feb662d64`
- **API:** 1.4.0 (variant 1) — `ImpellerGetVersion()` = `541081600`
- **Source:** `https://storage.googleapis.com/flutter_infra_release/flutter/cda25ea220c923a601dbc33ed4e9dd4feb662d64/linux-x64/impeller_sdk.zip`
- **License:** `LICENSE.sdk.md` (BSD-3-Clause, alongside this file)

To bump, in the NImpeller submodule:
`./build.sh DownloadImpeller --impeller-sha <sha> --all`
`./build.sh GenerateBindings --platform linux-x64`
