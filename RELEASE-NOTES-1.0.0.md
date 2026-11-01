# Shuddhi Image Builder 1.0.0

The first packaged x64 release of Shuddhi Image Builder creates customized
Windows 11 installation media through a native Windows wizard.

- Choose Balanced, Suddhi Plus or Custom, and select individual image items.
- Choose Keep, Disable or experimental Remove for Defender and Windows Update.
- Suddhi Plus includes the Balanced removals, removes Windows Update, and
  disables selected additional services and drivers; Defender is selectable.
- Use light, dark or system appearance, recipe save/load, logs and recovery.
- Download an official image in the app or supply your own Windows 11 media.

**Validation:** the release source passed 187 selected automated regression
tests on a non-elevated Windows host. One privileged native recovery test was
excluded because the host lacked `SeBackupPrivilege`. The predecessor build
opened successfully. This specific 1.0.0 package has not been used to complete
an ISO build or VM boot test. Test images in a disposable VM.

**Limits:** no 2 GB size guarantee. Experimental component removal can break
boot, hardware, apps and servicing. Defender may not function or update as
expected when dependencies or Windows Update are changed. Suddhi Plus avoids
blanket component-store pruning, but keeping Defender does not prove it works.

**Download:** extract the whole `Shuddhi-1.0.0-win-x64.zip` and run
`Shuddhi.exe`. Administrator access is required. No Windows ISO, activation key
or ADK executable is included. The portable application is unsigned. Its source
and binary derive from the upstream project noted in `NOTICE.md`.
