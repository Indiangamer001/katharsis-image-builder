# Shuddhi WinUI

Open `Shuddhi.exe` from the fully extracted release folder. This version uses the upstream Tiny11 Builder WinUI
wizard and its light, dark and system appearance settings. Choose **Custom**
to select individual removals. In both **Balanced** and **Custom**, open
**Build settings → Defender and Windows Update** for the independent choices.

**Suddhi Plus** replaces the former Core preset. It includes every Balanced
removal group and disables these image services/drivers when present:
`BthAvctpSvc`, `Ccx01000`, `diagsvc`, `IsoSessionCore`, `MapsBroker`, `PcaSvc`,
`RemoteRegistry`, `Spooler`, `ssh-agent`, `WMPNetworkSvc`, and
`dmwappushservice`. Windows Update removal is mandatory in Plus. Defender
defaults to Keep and can be set independently to Disable or Remove. Printing,
Bluetooth media controls, some cameras, app compatibility, diagnostics and
device management may be affected; test the specific target machine in a VM.
Missing optional service entries are skipped; missing mandatory Update targets
make the build fail rather than claim removal.

Plus does not apply the former Core preset's blanket component-store pruning,
which can remove files Defender needs. It therefore does not promise a 2 GB
ISO. Keeping Defender means Shuddhi does not deliberately disable/remove it;
actual protection, security intelligence updates and boot still need testing.
Automatic Defender intelligence updates can be affected when Update is removed.

When either choice is Disable or Remove, the builder automatically scans the
selected edition after confirmation to identify targets, then revalidates the
actions against the mounted working image. The scan can take a long time.
Do not also select individual Defender/Update targets in the advanced list;
use the dedicated choices for those components. The old irreversible
component-store pruning remains a separate Custom-only action. It cannot be
combined with the independent choices and disables Defender. Existing old Core
recipes load as Custom with that legacy action, requiring renewed consent.

Saved recipes retain these choices; changing image or edition requires a new
inventory. Remove is experimental targeted deletion, not a supported complete
Windows uninstall. It can break boot, servicing and applications, and cannot
promise a 2 GB result or that future repair will not restore components.
Defender settings can be ignored or overridden. Test in a disposable VM.

Copy the entire release folder to another Windows x64 computer to retain all
runtime dependencies. The source includes a separate PowerShell selector for
advanced users; the graphical WinUI application is the main entry point.
See `NOTICE.md` before distributing upstream source or derivative binaries.
