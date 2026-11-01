# JackMiner

JackMiner is an experimental Windows blackjack game with an optional, separately consented CPU resource session. The blackjack balance is virtual and has no cash value.

## Alpha-test branch

The `alphatest` branch contains the WPF blackjack table, first-run notice, setup controls and optional resource-session integration.

- One WPF main window contains the game, first-run notice, resource meter and session controls.
- The first-run notice is an overlay in that same window. Acknowledgement is stored under the user's Local AppData folder.
- Mining is **not** started by accepting the notice. It requires a separate opt-in checkbox, verified runtime files and a confirmation every time a session starts.
- Download / Repair Required Files retrieves the official XMRig Windows x64 release and checks its SHA-256 from the official XMRig release API before extraction.
- Every extracted runtime file is hashed and checked before each start. Files are stored under the user's Local AppData folder, not in the repository.
- Check for Updates compares the installed version with the latest official release and asks before replacing files.
- The runtime uses a loopback-only XMRig HTTP API with a random bearer token to adjust its CPU thread target as the blackjack bankroll changes.
- The CPU target is a **thread-count hint**, not a precise CPU utilisation limiter. Actual usage varies by CPU topology, cache, algorithm and operating system scheduling.
- Large Pages and MSR optimisations are disabled in this alpha until separate permission and diagnostics controls are implemented.
- The session runs as a child process supervised by JackMiner's `PenaltyProcessHost` using a Windows Job Object. The child has its own PID in Task Manager; closing JackMiner terminates the job and its child processes.
- No Windows service, detached console, tray helper, extra launcher process or Tor integration is used.

## Blackjack rules

- €1,000 virtual starting bankroll.
- Chip values: €5, €10, €25, €50, €75, €100 and €200.
- Maximum opening wager: €300.
- Six-deck shoe using a cryptographically secure random shuffle.
- Natural blackjack pays 3:2.
- Dealer stands on soft 17.
- Hit, stand, double down and one split per round.
- No insurance, deposits, withdrawals or cash value.

## Resource-session defaults

- Pool: `gulf.moneroocean.stream:10008`
- Worker name: `RAT KATFLOW`
- Wallet: enter your own MoneroOcean receiving address in the app. No wallet address, private key or seed phrase is stored in this repository.
- The pool and worker are editable locally. The wallet is a public receiving address and is stored in the user's Local AppData settings file.
- The default CPU target curve is 75% at €0, 55% at €1,000, and 15% at €3,000 or more. These percentages are passed as XMRig's maximum-thread hint, not guaranteed CPU percentages.

## Security and resource use

JackMiner does not disable Microsoft Defender, add antivirus exclusions, bypass UAC or silently restore quarantined files. If Windows Security quarantines a runtime file, review the alert yourself. Use the explicit repair button only after deciding the download is appropriate.

The local XMRig API listens only on 127.0.0.1 and uses a random per-session bearer token. The local runtime config contains the public wallet address and worker settings. Do not put a private key or seed phrase into the wallet field.

Hardware load, heat, fan noise, electricity use and wear are real even though the game balance is fictional. XMRig's CPU thread hint cannot guarantee a hard percentage cap.

## Build

- Visual Studio 2022 or the .NET 8 SDK.
- Windows desktop development / WPF workload.
- Open `JackMiner/JackMiner.csproj` and build for Windows.

## Current limitations

This branch still needs a successful Windows CI build and hands-on testing. MSR and Large Pages controls, pool connection diagnostics, session hashrate / accepted-share display, bankroll persistence, game history, audio, animations and production packaging are not complete. Do not treat this alpha as production-ready.
