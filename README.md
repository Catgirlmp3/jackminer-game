# JackMiner

JackMiner is a Windows desktop blackjack beta with an optional CPU-mining component planned for a later milestone. The bankroll is virtual and has no cash value. This is experimental software, not a production release.

## Current implementation

The first playable WPF blackjack table is being built in `JackMiner/`:

- €1,000 virtual starting bankroll.
- Chip values: €5, €10, €25, €50, €75, €100 and €200.
- Maximum opening wager: €300.
- Six-deck shuffled shoe using a cryptographically secure random shuffle.
- Natural blackjack pays 3:2.
- Dealer stands on soft 17.
- Hit, stand, double down and one split per round.
- Insurance is not offered.
- Virtual balance only; no deposits, withdrawals or cash value.

## Launch

The planned entry point is `JackMiner.vbs`. It displays a beta warning and then requests administrator consent through the standard Windows UAC prompt. The application manifest also requests administrator rights if the executable is launched directly.

Elevation is intended to support the optional CPU-mining feature in later builds. The current blackjack-only milestone does not need mining to play.

## Mining integration — planned, not yet implemented

Mining is not currently included in the playable game. When added, it will use the official prebuilt Windows x64 XMRig release and the owner's MeowKit MoneroOcean profile as editable defaults:

- Pool: `gulf.moneroocean.stream:10008`
- Wallet: the MoneroOcean receiving address from the owner's existing MeowKit configuration
- Worker: `RAT KATFLOW`

**No Tor integration is planned.** JackMiner will connect directly to the configured pool.

Future mining requirements:

- Download / Repair Required Files and Check for Updates.
- Verify official release checksums and the installed executable's SHA-256.
- Preflight required files before starting a game/mining session.
- Clearly report missing or quarantined files; never silently restore quarantined files or bypass Defender.
- MSR and Large Pages diagnostics with accurate permission/reboot explanations.
- Mining is opt-in, visible and managed as a child process by JackMiner. Never install a separate Windows XMRig service.
- Smooth, bounded bankroll-based CPU utilisation; stop the miner cleanly when disabled or when JackMiner exits.

## Security

JackMiner must not disable Microsoft Defender, tamper with security settings, or add antivirus exclusions automatically. Mining software can trigger antivirus or potentially-unwanted-application detections. Users must be shown clear warnings and verifiable build information and make their own informed decisions.

## Build

- Visual Studio 2022 or the .NET 8 SDK.
- Windows desktop development / WPF workload.
- Open `JackMiner/JackMiner.csproj` and build for Windows.

## Beta status

The repository is an early beta. The first game implementation is under active development and has not yet been compiled or tested on a Windows machine. Mining, updater, integrity preflight, persistence and production packaging remain future work.
