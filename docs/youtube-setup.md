# YouTube setup

> **Not in the current release.** The direct YouTube push is still in testing and is not built
> into the released plugin yet. This guide is here so the setup is ready when it ships. For now,
> use the text-block export and paste it into your description by hand. No login needed.

The YouTube push writes chapter timestamps into a video's description. That is a write to your
channel, so it needs a real OAuth login. You supply your own credentials from a Google Cloud
project; the plugin ships no shared secret. Setup is a one-time job of about ten minutes.

Read [SECURITY.md](../SECURITY.md) first so you know what access you are granting.

## 1. Create a Google Cloud project

1. Go to https://console.cloud.google.com and create a new project (any name).
2. Open "APIs & Services" > "Library", search for **YouTube Data API v3**, and enable it.

## 2. Configure the consent screen

1. "APIs & Services" > "OAuth consent screen".
2. Pick **External**, fill in the required app name and your email, and save.
3. On the "Test users" step, add the Google account whose channel you will edit.

Leaving the app in **Testing** is fine for personal use. One caveat: in Testing mode the refresh
token expires after about seven days, so you will re-authorise roughly weekly. Publishing the
app to Production removes that, at the cost of Google's verification process. For a personal,
single-user setup, Testing with weekly re-auth is the simpler path.

## 3. Create an OAuth client

1. "APIs & Services" > "Credentials" > "Create credentials" > "OAuth client ID".
2. Application type: **Desktop app**.
3. Copy the **client id** and **client secret**.

## 4. Connect in the plugin

1. `/clog cfg` > YouTube section.
2. Paste the client id and client secret.
3. Click Connect, read the warning, continue. A browser opens for the Google sign-in.
4. Choose when to push: when the duty ends, after every pull, or manual only.
5. Leave "Video id" blank to target your active live broadcast, or paste a specific video id.

## Quota

Each push costs about 51 units against the default 10,000 per day, so even long sessions barely
register. Pushing once at the end of a duty, or once per pull, is well within budget.

## Revoking access

Click Disconnect in the plugin (this deletes the local token), and/or remove the app at
myaccount.google.com under Third-party access.
