# JackMiner

JackMiner is an experimental Windows blackjack game where the house's CPU resource pressure is a core gameplay mechanic. The blackjack bankroll is fictional; CPU load, heat, fan noise and electricity use are real.

## Alpha-test branch

- The felt is visibly locked at launch. A confirmed resource session is required, then the player must separately accept the eerie table-unlock prompt. Stopping the session locks the table again.
- Nothing starts silently: install the verified runtime, choose a resource mode, then confirm before each session.
- Four modes set the XMRig CPU worker target:
  - Easy: 20% maximum
  - Medium: 50% maximum
  - Hard: 80% maximum
  - Beta: 95% maximum, labelled "O kurwa."
- The bankroll dynamically reduces the live worker target below the selected mode maximum. At €0, it reaches the mode maximum; at €3,000 or more, it backs off to 20% of that maximum.
- RandomX uses an explicit XMRig worker profile based on physical CPU cores. The number of workers is rounded to a whole core; XMRig's percentage hint remains in place for other supported algorithms. There is no Windows Job Object CPU-rate cap, so the miner is not being throttled a second time by the operating system.
- The Windows Job Object remains in use only to supervise the miner process and terminate descendants when JackMiner closes.
- The public Monero receiving address supplied for this build is hardcoded in the application. It is not a private key or seed phrase, but it is visible in this public source repository and compiled application.
- Download / Repair Required Files retrieves the official XMRig Windows x64 release and checks its SHA-256 from the official release API before extraction.
- Every extracted runtime file is hashed and checked before each start. Files are stored under the user's Local AppData folder, not in the repository.
- Check for Updates compares the installed version with the latest official release and asks before replacing files.
- The runtime uses a loopback-only XMRig HTTP API with a random bearer token to update the worker profile and pool port as bankroll or recovery state changes.
- RandomX yield is disabled to prefer hashrate, and Huge Pages are enabled where Windows permissions allow; XMRig can fall back if they are unavailable. MSR read/write optimisations remain disabled.
- MoneroOcean pool ports follow the selected mode: Easy 10001, Medium 10004, Hard 10008 and O kurwa 10016. The full-CPU bailout uses 10032.
- The session runs as a child process supervised by `PenaltyProcessHost` using a Windows Job Object. The child has its own PID in Task Manager; closing JackMiner terminates the job and its child processes.
- No Windows service, detached console, tray helper, extra launcher process or Tor integration is used.

## First launch: how to play

1. Accept the first-run notice. It does not start anything.
2. Press **Download / Repair Required Files** and let JackMiner verify the official XMRig Windows x64 package.
3. Choose Easy, Medium, Hard or Beta. These are XMRig worker-allocation targets, not harmless visual difficulty settings. RandomX worker allocation is rounded to whole physical cores.
4. Press **Enter Table + Start Resource Session** and confirm the warning. The resource process starts as a supervised child process.
5. In the right-hand **TABLE** tab, press **Unlock Table** and accept the separate table warning.
6. Select chips, then press **DEAL**. Use Hit, Stand, Double or Split when those actions are available.
7. Press **Stop Resource Session** to stop the workload. This locks the table again.

The updater is manual. It checks the official release version when **Check for Updates** is pressed, and it only offers an install when the official version is numerically newer than the verified installed version. Matching versions do nothing; a newer installed version is never downgraded. The first-run prompts do not run the updater.

## Blackjack rules

- €1,000 virtual starting bankroll, persisted locally between launches.
- The Oddities Shop sells permanent collectibles (minimum €100) such as an industrial tub of mayo, suspicious pickles, a hyper-specific F-16 configuration, a 2003 Toyota Corolla and jarred armpit hair. Each item permanently reduces the House Drop cooldown in proportion to its price; combined reductions are capped at 60 seconds.
- A manually claimed €25 House Drop normally becomes available every 15 minutes. Owned shop items permanently reduce that cooldown in proportion to their price; the combined reduction is capped at one minute. Drops do not stack, and the next claim time is saved locally.
- At €0, the Bailout tab offers full-CPU contracts: 10 minutes for €100, 25 minutes for €300, 30 minutes for €400, or 60 minutes for €1,500 in virtual bankroll credit. Credit is awarded only when the selected timer completes. The House Drop clock pauses during the contract and resumes afterward. Stopping the miner cancels the contract without paying it.
- Chip values: €5, €10, €25, €50, €75, €100 and €200.
- Maximum opening wager: €300.
- Six-deck shoe using a cryptographically secure random shuffle.
- Natural blackjack pays 3:2.
- Dealer stands on soft 17.
- Hit, stand, double down and one split per round.
- No insurance, deposits, withdrawals or cash value.

## Security and resource use

JackMiner downloads the resource runtime from the official XMRig release feed and verifies the published SHA-256 checksum before installing it. Antivirus products may flag mining software as potentially unwanted. Review any alert in Windows Security before deciding whether to allow the file.

The local XMRig control API listens only on loopback and uses a random per-session bearer token. Resource routing is fixed inside the application and is not exposed as a player setting.

The selected mode is an XMRig worker target, not a Windows-enforced CPU-rate cap. RandomX worker counts are rounded to whole physical cores, so the realised allocation is necessarily discrete. Other algorithms use XMRig's max-threads hint. The game balance changes the live target within the chosen maximum. The 95% beta mode can make the PC feel heavily loaded and may cause substantial heat, fan noise and power consumption. The Bailout contract raises the CPU worker allocation to all detected physical cores for its selected duration; it does not use the GPU.

## Build

- Visual Studio 2022 or the .NET 8 SDK.
- Windows desktop development / WPF workload.
- Open `JackMiner/JackMiner.csproj` and build for Windows.

## Current limitations

This is an experimental alpha and still needs hands-on testing of XMRig worker allocation on different CPUs, pool connectivity and difficulty-port selection, timed bailout contracts, mode switching and long-session stability. RandomX worker allocation is discrete because workers are whole threads; it is not an exact fractional Windows Task Manager percentage. Actual hashrate depends on CPU model, cooling, memory configuration, algorithm selection and whether Windows grants Huge Pages. MSR tuning, game history, audio, animations and production packaging are not complete.
