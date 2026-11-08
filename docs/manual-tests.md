# Manual tests

Automated tests never open anything on the computer: they replace the system with a fake. These checks cover what only a real Windows session can show. Run them before a release, with the build that will ship.

Prepare a test folder with a text file, a subfolder whose name has spaces and accents, and a batch file (`.bat`) that contains only `exit`. Close Calculator, Character Map and Notepad first, so that you can tell what the restoration opened.

## Restoration

| Check | Expected | Last verified |
|---|---|---|
| Add Calculator from **Choose an installed application** | The search ignores accents; the name fills in; the resource shows "Application installée sur cet ordinateur" | 2026-11-08, Windows 11 |
| Add Character Map the same way | Same as above (a desktop application, not a packaged one) | 2026-11-08 |
| Add the text file and the subfolder | Their names are suggested from the paths | 2026-11-08 |
| Add the batch file as a file | The form refuses it and explains that programs are added as applications | 2026-11-08 |
| Add a file that does not exist | It is accepted; existence is checked when restoring | 2026-11-08 |
| Restore | A confirmation lists the two applications before anything starts | 2026-11-08 |
| Confirm | Calculator and Character Map start, the text file opens in Notepad, the folder opens in Explorer; the report says 4 restored, 1 missing | 2026-11-08 |
| Restore again with the applications still open | Nothing is closed; the applications are started again or brought forward by Windows | Not yet verified |
| Add a link and restore | It opens in the default browser | Not yet verified: the test avoids touching the browser of the person running it |
| Add a folder on a network share that does not answer | The report says Windows did not answer in time, after about 15 seconds, and the other resources open | Not yet verified |
| Add a program that stops with an error at once | The report says it stopped right after starting, with its exit code | Not yet verified |
| Add a program that asks for administrator rights, then refuse the prompt | The report says Windows refused to open it | Not yet verified |
| Stop a restoration while it runs | What was opened stays open; the rest is reported as skipped | Covered by automated tests only |

## Interface

| Check | Expected | Last verified |
|---|---|---|
| Tab through the home page | The focus is always visible | 2026-11-08 |
| Enter in the quick-create field | Creates the workspace | 2026-11-08 |
| Escape in a confirmation | Cancels; the focus starts on the safe answer | 2026-11-08 |
| Close the window right after typing notes | The notes are there after a restart | 2026-11-06 |
| Light and dark themes, and following the system | Every page stays readable; the title bar follows on Windows 11 | 2026-11-06 |
| Smallest window (800 × 520) | Nothing overlaps; long names wrap or end with an ellipsis | 2026-11-06 |
| Start Atlas a second time | A message says it is already open; the data is untouched | 2026-11-08 |
