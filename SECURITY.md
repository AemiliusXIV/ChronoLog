# Security

## Reporting a vulnerability

Found a security issue? Please open a private report through GitHub's Security advisories
("Report a vulnerability" on the repo's Security tab) rather than a public issue. Include what
you found and how to reproduce it. I'll respond as soon as I can.

## What the plugin can access

- Reads game state (boss HP, casts, deaths) locally. None of it leaves your machine.
- Talks to OBS only on the address you configure, normally localhost.
- Talks to YouTube only if you turn that feature on, using OAuth credentials you supply.

## YouTube OAuth: the risk, stated plainly

The YouTube push is not in the current release yet; it is still in testing. When it ships it
will be opt-in and carries a real trade you should understand.

- **Scope is coarse.** YouTube has no description-only permission. The scope this plugin
  requests (`youtube`) grants broad management of your channel, including editing and deleting
  videos. The plugin only edits video descriptions, but the access token is more powerful than
  that single use.
- **The token lives on disk.** The refresh token is stored under the plugin's config folder on
  your PC. Treat it like a credential. If this machine is compromised, the token could be used
  against your channel.
- **You bring your own client.** Credentials come from a Google Cloud project you create; there
  is no shared secret baked into the plugin.
- **Revoke any time.** Remove access at myaccount.google.com under Third-party access, or click
  Disconnect in the plugin (which also deletes the local token).

If you would rather not hold a token at all, use the text-block export instead. It needs no
login and no credentials.

## Secrets

No API keys, tokens or client secrets are committed to this repository. YouTube credentials are
entered at runtime and stored locally, outside source control.
