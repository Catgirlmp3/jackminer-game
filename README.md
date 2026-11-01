# JackMiner

JackMiner is an experimental Windows blackjack game where the house's CPU resource pressure is a core gameplay mechanic. The blackjack bankroll is fictional; CPU load, heat, fan noise and electricity use are real.

## Alpha-test branch

- The felt is visibly locked at launch. A confirmed resource session is required, then the player must separately accept the eerie table-unlock prompt. Stopping the session locks the table again.
- Nothing starts silently: install the verified runtime, choose a resource mode, then confirm before each session.
- Four modes set a Windows Job Object **hard CPU-rate ceiling** for the complete resource process group:
  - Easy: 20% maximum
  - Medium: 50% maximum
  - Hard: 80% maximum
  - Beta: 95% maximum, labelled "O kurwa."
- Players choose a preset house-pressure mode, not a raw CPU percentage. There is no editable wallet field or CPU slider; the built-in payout identity is fixed, and the bankroll dynamically reduces the live cap below the selected mode maximum. At €0, the cap reaches the mode maximum; at €3,000 or more, it backs off to 20% of that maximum.
- The CPU hard cap is applied by Windows to the child process group. XMRig's thread hint is supplementary; the Job Object is the enforcement mechanism.
- Pool: `moneroocean.stream:10001`
- Worker: `JACKMINER`
- The public Monero receiving address supplied for this build is hardcoded in the application. It is not a private key or seed phrase, but it is visible in this public source repository and compiled application.
- Download / Repair Required Files retrieves the official XMRig Windows x64 release and checks its SHA-256 from the official release API before extraction.
- Every extracted runtime file is hashed and checked before each start. Files are stored under the user's Local AppData folder, not in the repository.
- Check for Updates compares the installed version with the latest official release and asks before replacing files.
- The runtime uses a loopback-only XMRig HTTP API with a random bearer token to update the supplementary thread hint as the bankroll changes.
- MSR and Large Pages optimisations remain disabled.
- The session runs as a child process supervised by `PenaltyProcessHost` using a Windows Job Object. The child has its own PID in Task Manager; closing JackMiner terminates the job and its child processes.
- No Windows service, detached console, tray helper, extra launcher process or Tor integration is used.

## Blackjack rules

- €1,000 virtual starting bankroll, persisted locally between launches.
- The Oddities Shop sells permanent collectibles (minimum €100) such as an industrial tub of mayo, suspicious pickles, a hyper-specific F-16 configuration, a 2003 Toyota Corolla and jarred armpit hair. They have no gameplay effect yet; they are reserved for a later collection feature.
- A manually claimed €25 House Drop becomes available every 15 minutes while the game is open. Drops do not stack, and the next claim time is saved locally.
- Chip values: €5, €10, €25, €50, €75, €100 and €200.
- Maximum opening wager: €300.
- Six-deck shoe using a cryptographically secure random shuffle.
- Natural blackjack pays 3:2.
- Dealer stands on soft 17.
- Hit, stand, double down and one split per round.
- No insurance, deposits, withdrawals or cash value.

## Security and resource use

JackMiner does not disable Microsoft Defender, add antivirus exclusions, bypass UAC or silently restore quarantined files. If Windows Security quarantines a runtime file, review the alert yourself. Use the explicit repair button only after deciding the download is appropriate.

The local XMRig API listens only on 127.0.0.1 and uses a random per-session bearer token. The local runtime config contains the public wallet address and worker settings. Do not put a private key or seed phrase into the wallet field.

The selected mode is a maximum CPU rate for the resource process group, not a target for the entire PC. Other applications are outside JackMiner's job object. The game balance only changes JackMiner's live cap within the chosen maximum. The 95% beta mode can make the PC feel heavily loaded and may cause substantial heat, fan noise and power consumption.

## Build

- Visual Studio 2022 or the .NET 8 SDK.
- Windows desktop development / WPF workload.
- Open `JackMiner/JackMiner.csproj` and build for Windows.

## Current limitations

This is an experimental alpha and still needs successful Windows CI and hands-on testing of the Job Object CPU hard cap, pool connectivity, payout worker, mode switching and long-session stability. MSR and Large Pages controls, pool diagnostics, session hashrate / accepted-share display, game history, audio, animations and production packaging are not complete. Collectibles are currently inventory-only and have no gameplay effects.
