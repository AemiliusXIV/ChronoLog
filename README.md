# ChronoLog

A Dalamud plugin that watches raid duties and turns every pull, wipe and clear into a
timestamp. It can drop OBS chapter markers as you go, build a YouTube-description block you
paste in, or push chapters straight into a live stream's description.

The point: stop hand-logging "pull 14, wipe at 8%" while you raid. The plugin reads it from
the game and writes the list for you.

## What it captures

Each pull records the fight, the phase it ended in, the boss HP % at the wipe, the lowest HP
reached, the first death (and what killed them when known), and the pull length. Quick resets
under a configurable threshold are dropped so the list stays clean.

## Outputs

- **OBS chapter markers.** Connects to OBS over websocket v5 and drops a native chapter at the
  start of each pull. Chapters need OBS 30.2 or newer recording to Hybrid MP4 to embed; without
  that the plugin still records timestamps for the other two outputs.
- **Text block.** A YouTube-ready list of timestamps, copied to the clipboard or written to a
  file. The first line is always 0:00 so YouTube reads the chapters. The line format is a
  template you control.
- **YouTube push (coming soon).** Planned for a future release: write the chapter block directly
  into a live stream's video description through the YouTube Data API, automatically after each
  pull or on clear. No manual copy-paste needed.

## Setup

1. Install, then open settings with `/clog cfg`. `/clog` opens the session window.
2. For OBS: see [docs/obs-setup.md](docs/obs-setup.md) for a step-by-step walkthrough.
3. For YouTube: this needs your own Google Cloud OAuth client. See
   [docs/youtube-setup.md](docs/youtube-setup.md). Read the warning below first.

## YouTube access and your account

Editing a video description is a write to your channel, so YouTube requires a full OAuth login,
not just an API key. Two things to know before you connect:

- YouTube has no "edit description only" permission. The narrowest scope that works still grants
  broad management of your channel. The plugin only ever edits descriptions, but the token it
  holds can do more.
- The token is stored on this PC only. Nothing is sent anywhere except OBS on your own machine
  and Google's official API under your own credentials. Revoke access any time at
  myaccount.google.com under Third-party access.

If that trade is not for you, skip the YouTube push and use the text block. It needs no login.

## Privacy

Everything the plugin reads (boss HP, casts, deaths) stays local. The only outbound traffic is
to OBS on localhost and, if you turn it on, the YouTube Data API using credentials you supply.
No telemetry, no third-party servers.

## Not affiliated with Square Enix

FINAL FANTASY XIV is a registered trademark of Square Enix Holdings Co., Ltd.
This is an unofficial, fan-made tool with no affiliation to or endorsement by Square Enix. It is
a third-party add-on for the Dalamud plugin framework; use it at your own discretion.
