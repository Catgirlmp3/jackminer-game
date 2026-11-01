# JackMiner

JackMiner is a Windows desktop blackjack alpha with a planned, optional CPU-mining mechanic. The bankroll is virtual and has no cash value. This is experimental software, not a production release.

## Alpha-test branch

The `alphatest` branch contains the current WPF game and the first-run warning / resource-tax UI experiment.

- One WPF main window contains the table, controls, first-run notice and resource meter.
- The first-run notice is an overlay in that same window, not a second application window. Acknowledgement is stored under the user's Local AppData folder.
- The resource meter previews a smooth bankroll-to-CPU-target curve: lower bankroll means a higher future target; higher bankroll means a lower target.
- **The miner is not implemented in this alpha snapshot.** The bar is a preview only and no CPU mining is started.
- The warning does not enable mining. Any future miner must require a separate explicit opt-in.

## Blackjack rules

- €1,000 virtual starting bankroll.
- Chip values: €5, €10, €25, €50, €75, €100 and €200.
- Maximum opening wager: €300.
- Six-deck shoe using a cryptographically secure random shuffle.
- Natural blackjack pays 3:2.
- Dealer stands on soft 17.
- Hit, stand, double down and one split per round.
- No insurance, deposits, withdrawals or cash value.

## Planned mining integration

When implemented, mining will use the official prebuilt Windows x64 XMRig release and the owner's MeowKit MoneroOcean profile as editable defaults:

- Pool: `gulf.moneroocean.stream:10008`
- Wallet: the receiving address from the owner's existing MeowKit configuration; not copied into this repository.
- Worker: `RAT KATFLOW`
- No Tor integration; direct pool connection only.

Future requirements:

- Download / Repair Required Files and Check for Updates.
- Verify official release checksums and installed executable SHA-256.
- Preflight required files before starting game/mining.
- Explain missing or quarantined files; never silently restore quarantined files or bypass Defender.
- MSR and Large Pages diagnostics with accurate permission / reboot explanations.
- Mining is separately opt-in, visible, capped and managed by JackMiner as a child process. No Windows service, detached console, tray helper or extra launcher process.
- Use a Windows Job Object so the child miner is owned by JackMiner and is terminated when JackMiner exits. The child process may still appear as a child in Task Manager's process tree; Windows does not guarantee every view will collapse it into one row.
- Smooth, bounded bankroll-based CPU utilisation. The displayed meter is a target preview, not a live CPU sensor.

## Security and resource use

JackMiner must not disable Microsoft Defender, tamper with security settings, or add antivirus exclusions automatically. Mining software can trigger antivirus or potentially-unwanted-application detections. Hardware load, heat, fan noise, electricity use and wear are real even though the game balance is fictional.

## Build

- Visual Studio 2022 or the .NET 8 SDK.
- Windows desktop development / WPF workload.
- Open `JackMiner/JackMiner.csproj` and build for Windows.

## Beta status

This branch has not yet been compiled or tested on Windows. Mining, updater, integrity preflight, persistence of bankroll / game history, audio, animations and production packaging remain unfinished.
