# YouTube setup

The YouTube push reads the current session's pull list and rewrites a marked section of a
video's description with chapter timestamps. It targets your active live broadcast automatically,
or a specific video id you set. Each push replaces the previous block so the description stays
clean no matter how many wipes you have.

The feature is opt-in and needs your own Google Cloud OAuth credentials. The plugin ships no
shared secret; every user brings their own. Setup takes about ten minutes and is a one-time job.

Read [SECURITY.md](../SECURITY.md) before continuing. It covers what access you are granting
and the risks of holding an OAuth token.

---

## 1. Create a Google Cloud project

1. Go to https://console.cloud.google.com and sign in with the Google account that owns the
   YouTube channel you stream from.
2. Click the project selector at the top, then **New Project**. Name it anything ("ChronoLog" works).
3. In the search bar, search for **YouTube Data API v3** and open it. Click **Enable**.

---

## 2. Configure the OAuth consent screen

1. In the left sidebar, open **Google Auth Platform** (or search for it in the top bar).
2. Click **Get started**, pick **External** as the user type, and fill in:
   - App name: anything ("ChronoLog" is fine)
   - User support email: your own address
   - Developer contact: same address
3. Save through the wizard. On the **Audience** page (or the "Test users" step), scroll to
   **Test users** and add the Google account you will sign in with when connecting the plugin.

> **Testing mode and the seven-day limit.** Leaving the app in Testing is fine for personal use.
> The trade-off: Google refreshes tokens in Testing mode for at most seven days, after which the
> plugin will show an auth error and you need to click Connect again. Publishing the app to
> Production removes the limit, but Google then runs a review process. For a single-user setup,
> Testing with occasional re-auth is the simpler path.

---

## 3. Create an OAuth client

1. In the left sidebar, open **APIs & Services > Credentials**.
2. Click **Create credentials > OAuth client ID**.
3. Application type: **Desktop app**. Give it any name.
4. Copy the **Client ID** and **Client secret** from the confirmation dialog. You will paste them
   into the plugin settings.

---

## 4. Connect in the plugin

1. Run `/chrono cfg` in-game and scroll to the **YouTube** section.
2. Paste the Client ID and Client secret into the matching fields.
3. Choose when to push:
   - **The duty ends:** one push per clear or abandoned run. Low API cost.
   - **After every pull:** updates after each wipe. Higher cost but the description stays live.
   - **Manual only:** use the Push button in the session window whenever you want.
4. Leave **Video id** blank to target your active live broadcast automatically. If you want to
   push to a non-live video, paste its id here.
5. Click **Connect**, read the warning modal, and click **Continue**. A browser opens for the
   Google sign-in. Sign in, grant access, and close the browser tab.

The status in the settings window changes to **Authorised** once the token is saved.

> **On plugin reload.** The token is stored locally; if you reload or update the plugin while
> YouTube is enabled, it reconnects silently without opening the browser again.

> **Cancelling.** If the browser sign-in hangs or you want to abort, click **Cancel** in the
> settings window rather than reloading the plugin.

---

## How the description update works

When a push fires, the plugin:

1. Reads the current description of the target video.
2. Looks for a `==== Pull timestamps ====` line.
3. If found, replaces everything from that line onward with the new block. Anything above it
   (your normal description, links, etc.) is left intact.
4. If not found, appends the block at the end.

The first timestamp in the block is always `0:00` so YouTube reads the list as a chapter index.

---

## Quota

The YouTube Data API has a default quota of 10,000 units per day. Each push costs about 51 units
(one `videos.list` call plus one `videos.update`). Even a long prog session pushing after every
pull stays well under the daily limit.

---

## Revoking access

Click **Disconnect** in the plugin settings. This removes the local token and the plugin forgets
the authorisation. To also remove the app from your Google account, go to
myaccount.google.com under **Third-party apps & services**.

---

## Troubleshooting

**"Access denied" or 403 error on sign-in.** Your Google account is not listed as a Test user.
Go back to Google Auth Platform, open the Audience page, and add the account you are signing in
with to the Test users list. Then try connecting again.

**"No target video. Set a video id, or start a live broadcast."** The plugin found no active
live broadcast on your channel. Start streaming first, or paste the video id directly into the
Video id field in settings.

**"The service youtube has thrown an exception."** Usually means the token has expired (Testing
mode, seven-day limit). Click **Connect** again to re-authorise.

**Push fires but the description is not updating.** Check that the Video id field is blank (to
use the live broadcast) or points to the right video. Also check the Last error line in the
YouTube section of settings; it shows the most recent failure.
