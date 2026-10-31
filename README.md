# JackMiner

**JackMiner is a Windows desktop blackjack game with an optional, explicitly controlled CPU-mining workload.** It is currently a beta project. The bankroll is virtual and has no cash value.

## Project goals

- A polished, self-contained Windows desktop experience with an indie-game presentation.
- Blackjack with a €1,000 virtual starting bankroll and €300 maximum wager.
- Chips: €5, €10, €25, €50, €75, €100 and €200.
- Dealer stands on soft 17; natural blackjack pays 3:2; double down and split are supported; insurance is omitted.
- The agreed blackjack rules and probabilities remain consistent. House advantage comes from the published rules, not secretly manipulated cards or outcomes.
- Mining is optional, visible, bounded and controlled by JackMiner. It must never install or register a separate XMRig Windows service.
- No hidden persistence, process hiding, silent security changes or automatic antivirus exclusions.

## Windows launch flow

The planned entry point is `JackMiner.vbs`. The intended sequence is:

1. The user starts the VBS launcher.
2. A clear beta warning appears before elevation. It explains that JackMiner is experimental, includes an XMRig mining component, may trigger Microsoft Defender detections, and should only be continued if the user trusts the build and understands the workload.
3. The user explicitly chooses whether to continue.
4. Windows displays the standard UAC administrator-consent prompt.
5. If consent is granted, JackMiner starts. If the user declines, the launcher exits cleanly without leaving a miner or background process running.

The beta build is intended to request administrator permission up front to reduce avoidable XMRig permission/MSR troubleshooting. UAC must remain a genuine Windows consent prompt; JackMiner must not attempt to bypass it. The app should explain why elevation is requested.

## Security and Microsoft Defender

XMRig and other mining software can trigger antivirus or potentially-unwanted-application detections. JackMiner must not disable Defender, tamper with security settings, or add exclusions automatically. The beta warning should explain this before launch. Any exclusion is a decision the user makes themselves in Windows Security after verifying the build and understanding the risk. Provide hashes and build provenance where available; never tell users to ignore an unexplained detection.

## Mining lifecycle

- Mining is opt-in and its state/utilisation are visible in the application.
- JackMiner owns the miner process lifecycle: start, monitor, adjust and stop.
- Bankroll-based utilisation changes must be smooth, bounded and configurable rather than abrupt.
- The miner must be stopped cleanly when the application exits or the user disables mining.
- No standalone Windows service, hidden background task or automatic startup persistence.

## Beta status

This repository is a beta project, not a claim of production readiness. Expect bugs and incomplete features. Test releases should clearly identify their version/build and known limitations. The game should remain usable with mining disabled.

## Build

The Windows desktop client is built with the .NET SDK. See the project files for the exact target framework and publish configuration.

## Fairness

Blackjack outcomes use a documented shuffle and draw algorithm. No card manipulation, loss-forcing logic, or outcome overrides are permitted. Any house edge comes from the agreed blackjack rules and payouts, which should be documented in the game UI.
