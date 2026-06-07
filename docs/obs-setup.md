# OBS Setup Guide

ChronoLog talks to OBS over its built-in WebSocket server. This lets it drop chapter markers
directly into your recording or stream as each pull happens, with no manual input.

OBS WebSocket ships with OBS by default since version 28. If your OBS is up to date you already
have it - it just needs to be switched on.

---

## Basic setup

### 1. Enable the WebSocket server in OBS

Open OBS, then go to **Tools > WebSocket Server Settings**.

<!-- screenshot: OBS Tools menu open, WebSocket Server Settings highlighted -->

Tick **Enable WebSocket server**. The port defaults to **4455** - leave it as-is unless
something else on your PC is already using that port.

If you want a password (recommended), tick **Enable Authentication** and set one.

<!-- screenshot: WebSocket Server Settings dialog with Enable WebSocket server checked -->

Click **OK**.

### 2. Connect ChronoLog

In-game, run `/clog cfg` to open the settings window, then find the **OBS** section.

- **Host**: leave this as `127.0.0.1`. That points to OBS on the same machine. Only change it
  if OBS is running on a different PC on your network.
- **Port**: match whatever is set in OBS (default `4455`).
- **Password**: paste your OBS WebSocket password here, or leave blank if you did not set one.

<!-- screenshot: ChronoLog settings window, OBS section filled in -->

Click **Connect**. The status line changes to **Connected** in green when it works.

<!-- screenshot: ChronoLog settings window showing Connected status -->

That's it. ChronoLog will now drop a chapter marker at the start of each pull.

---

## Checking it works

Start a recording in OBS, then enter a duty. On the first pull you should see a new chapter entry
appear in OBS's recording timeline. If nothing appears, check:

- The status line in `/clog cfg` - if it shows an error, re-check the port and password.
- That OBS is actually running and not minimised to tray without the server starting.
- That no firewall is blocking localhost traffic on port 4455.

---

## Advanced topics

More detail on the following is coming. For now, the short version:

**Chapter markers embedding in recordings** - chapters only embed when OBS records to Hybrid MP4.
Change the recording format under OBS **Settings > Output > Recording**. Without this, timestamps
are still tracked and written to the text export; they just will not be clickable inside the
video file itself.

**Connecting to OBS on another PC** - set **Host** to the local IP of the machine running OBS
and make sure port 4455 is reachable across your network. Authentication is strongly recommended
when OBS is not on the same machine.

**Reconnection** - if OBS closes or the connection drops mid-session, ChronoLog will attempt to
reconnect once after a short delay and print the result to your chat log. If the reconnect fails
you will see a prompt to reconnect manually in `/clog cfg`.
