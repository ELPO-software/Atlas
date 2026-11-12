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

## Versions, history, import and export

| Check | Expected | Last verified |
|---|---|---|
| Save a version | Its suggested name is dated; it appears in the list | 2026-11-08 |
| Change the notes, then go back to the version | The notes come back; an automatic version keeps the changed state | 2026-11-08 |
| Export a workspace whose paths contain your user name | A warning names it before the Save dialog opens | 2026-11-08 |
| Export, then import the file | The preview lists every resource before anything is created; a new workspace opens | 2026-11-08 |
| Import a file that is not a workspace | The preview explains why and offers no Import button | Covered by automated tests only |
| Import a workspace with an application | The application is marked "À approuver" and skipped by a restoration until approved | Covered by automated tests only |
| Open the history | Every action above is listed, most recent first, including deleted workspaces | 2026-11-08 |

## Interface

| Check | Expected | Last verified |
|---|---|---|
| Tab through the home page | The focus is always visible | 2026-11-08 |
| Enter in the quick-create field | Creates the workspace | 2026-11-08 |
| Escape in a confirmation | Cancels; the focus starts on the safe answer | 2026-11-08 |
| Close the window right after typing notes | The notes are there after a restart | 2026-11-06 |
| Light and dark themes, and following the system | Every page stays readable; the title bar follows on Windows 11 | 2026-11-06 |
| Smallest window (860 × 560) | Nothing overlaps; long names wrap or end with an ellipsis; long paths are shortened in the middle | Not yet verified |
| Start Atlas a second time, with the first one minimized | The first window comes back, in the size it had; the second start opens nothing | 2026-11-12, automated by `build/test-installer.ps1` for the handover |
| Turn animations off in Windows, then change pages | Pages change without fading | Not yet verified |

## Environment detection

Use a data folder of your own (`ATLAS_DATA_DIR`), and prepare a folder that is a Git repository with an `origin` remote on GitHub and a text file.

| Check | Expected | Last verified |
|---|---|---|
| First start | The home page invites to allow the detection; nothing is observed before | 2026-11-12 |
| Allow it, then open a file of the repository in an application | The **Suggestions** page proposes the project as detected, with the application, the file, the folder, the solution and the repository address | 2026-11-12, the Atlas repository with an icon open in Paint |
| Leave Atlas in the background, installed | After a second scan, a Windows notification proposes the project, with **Enregistrer**, **Plus tard** and **Ignorer** | 2026-11-12, installed for the current user; Windows accepted the notification two minutes later |
| Choose **Enregistrer** in the notification | Atlas comes forward on the new workspace; the history says it came from a suggestion | Not yet verified on a real notification: the computer used had Do Not Disturb on; the choice itself is covered by automated tests |
| Open only the repository folder in File Explorer | The proposal is marked **Probable · à vérifier**, and its notification offers **Vérifier** instead of **Enregistrer** | Not yet verified |
| Keep Atlas in front | The proposal appears as a banner in Atlas, not as a notification | Covered by automated tests only |
| Turn Do Not Disturb on in Windows | The notification goes to the notification center without a banner | 2026-11-12, notification delivered with Do Not Disturb on |
| Close Atlas | Its notifications leave the notification center | 2026-11-12, notification center cleared by the same call |
| Run a copy that was not installed | The settings say Windows notifications are reserved to an installed Atlas; proposals show in Atlas | 2026-11-12 |
| **Oublier les observations** in the settings | After confirmation, the suggestions and the ignored list are empty; workspaces stay | Covered by automated tests only |

## Installers

| Check | Expected | Last verified |
|---|---|---|
| Run `./build/publish.ps1` | Two installers, two portable archives and `SHA256SUMS.txt` in `artifacts`; every binary of each copy is built for its processor | 2026-11-12 |
| Install for the current user, start, update in place, uninstall | `build/test-installer.ps1 -Scope CurrentUser` passes; the data folder is unchanged | 2026-11-12, Windows 11 x64 |
| The same for all users, and from an older version | `build/test-installer.ps1 -Scope AllUsers -OlderInstaller` passes | 2026-11-12, CI on Windows Server 2025 x64 and Windows 11 ARM64 |
| Run the ARM64 installer on an x64 computer, and the reverse | It refuses to install and names the right download | 2026-11-12, both ways (x64 here, ARM64 in CI) |
| Run the installer while Atlas is open | It asks to close Atlas first | 2026-11-12, refusal checked silently |
| Install with the wizard, in light and dark Windows themes | The wizard follows the theme; the choice between all users and the current user is offered; Atlas starts at the end if asked | Not yet verified |
| Look at **Applications installées** | ELPO Atlas, by ELPO software, with its icon and version | 2026-11-12, checked through the registry by the test script |
| Run it on a Windows 10 computer | Atlas installs, starts and restores; the title bar keeps the system color | Not yet verified |

## Portable archive

| Check | Expected | Last verified |
|---|---|---|
| Extract the archive to a folder whose name has spaces and accents, and run `Atlas.exe` with `dotnet` hidden from the environment | Atlas starts; its runtime is loaded from the extracted folder, not from an installed .NET | 2026-11-12, `build/test-portable.ps1` on x64 and ARM64 |
| In the settings, choose **Voir la licence** for a component | Its full license text opens | 2026-11-08 |
| Open the settings | The version is 0.2.0-beta.1; the licenses are listed, Inno Setup among them | 2026-11-12 |