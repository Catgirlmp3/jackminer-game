# JackMiner

**JackMiner is a Windows desktop blackjack game with an optional, explicitly controlled CPU-mining workload.** It is currently a beta project. The bankroll is virtual and has no cash value.

## Game

- Polished Windows desktop experience with an indie-game presentation.
- €1,000 virtual starting bankroll and €300 maximum wager.
- Chips: €5, €10, €25, €50, €75, €100 and €200.
- Dealer stands on soft 17; natural blackjack pays 3:2; double down and split are supported; insurance is omitted.
- Outcomes use a documented shuffle and draw algorithm. No manipulated cards or hidden outcome overrides.

## Mining defaults

JackMiner's initial mining profile follows the MoneroOcean settings used by the owner's MeowKit/UbuntuKit setup:

- Pool: `gulf.moneroocean.stream:10008`
- Wallet: the MoneroOcean receiving address configured in the owner's MeowKit miner profile.
- Worker label: `RAT KATFLOW`
- Pool password/worker metadata: `RAT KATFLOW`

These are defaults, not immutable values. Keep pool, wallet and worker settings in a clearly labelled configuration screen so the owner can change them without rebuilding the application. Never include wallet private keys or seed phrases. Mining must be explicitly enabled by the user.

## XMRig installation and integrity

- Download the official prebuilt Windows x64 XMRig release; do not compile it on every user's PC.
- Resolve the release from the official XMRig GitHub releases, verify the published checksum/signature where available, and record the installed version and SHA-256.
- Keep the downloaded archive and extracted executable in JackMiner's managed application-data directory.
- Provide **Download / Repair Required Files** and **Check for Updates** actions.
- Before each game launch, run a preflight integrity check on JackMiner's required app assets. If mining is enabled, also check that the expected XMRig executable and required files exist and match the recorded hashes.
- If XMRig is missing or changed, explain that it may have been quarantined or modified and offer a user-confirmed repair/re-download. Do not silently restore quarantined files, bypass Defender, add exclusions, or disable security controls.
- If mining files are unavailable, allow blackjack to launch with mining disabled; never launch an unknown or hash-mismatched miner.

## CPU optimisations / MSR

Provide a user-facing **Enable CPU Optimisations** control (not a misleading GPU/hardware-acceleration toggle). It should explain and test the relevant XMRig RandomX optimisations:

- MSR tuning is requested by XMRig at miner startup when supported and when the process has administrator rights. It is not a persistent setting that inherently requires a reboot; XMRig normally restores the original MSR values when it exits.
- Report whether MSR setup succeeded, was unsupported, or failed due to permissions/driver/platform restrictions. Do not repeatedly retry or silently change Secure Boot or other security settings.
- Large Pages is a separate Windows privilege/configuration requirement. If JackMiner offers to configure it, explain the Windows permission being changed and whether sign-out/reboot is required for it to take effect. Make changes only after explicit confirmation, and offer a clear way to undo them.
- Never claim that a reboot is required for MSR itself. Only request a reboot when a specific Windows setting actually requires it, and explain exactly why.

## Windows launch flow

The planned entry point is `JackMiner.vbs`:

1. User starts the VBS launcher.
2. A beta warning explains that JackMiner is experimental, includes an XMRig mining component, may trigger Microsoft Defender detections, and should only be continued if the user trusts the build and understands the workload.
3. User explicitly chooses whether to continue.
4. Windows displays the standard UAC administrator-consent prompt. The beta is intended to request elevation up front to reduce avoidable XMRig permission/MSR troubleshooting.
5. If consent is granted, JackMiner starts. If declined, it exits cleanly without leaving a miner or background process running.

UAC must remain a genuine Windows consent prompt; JackMiner must not attempt to bypass it. Explain why elevation is requested.

## Security and Microsoft Defender

XMRig and other mining software can trigger antivirus or potentially-unwanted-application detections because miners are abused by malware. JackMiner must not disable Defender, tamper with security settings, or add exclusions automatically. Explain detections before launch and provide build provenance and hashes where available. Any exclusion is a decision the user makes themselves in Windows Security after verifying the build and understanding the risk. Never tell users to ignore an unexplained detection.

## Mining lifecycle

- Mining is opt-in and its state/utilisation are visible in the application.
- JackMiner owns the miner process lifecycle: start, monitor, adjust and stop. No separate Windows service.
- Bankroll-based utilisation changes are smooth, bounded and configurable.
- The miner stops cleanly when the application exits or the user disables mining.
- No hidden background task or automatic startup persistence.

## Beta status

This repository is experimental beta software, not a claim of production readiness. Test releases should identify their version/build and known limitations. The game should remain usable with mining disabled.

## Build

The Windows desktop client is built with the .NET SDK. See the project files for the exact target framework and publish configuration.
