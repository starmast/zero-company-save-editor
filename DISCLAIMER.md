# Disclaimer

**This is an unofficial, fan-made, non-commercial tool. It is not affiliated with, endorsed by, sponsored by or
approved by The Walt Disney Company, Lucasfilm Ltd., Electronic Arts Inc., Bit Reactor, or any of their
affiliates.**

## Trademarks and ownership
STAR WARS, STAR WARS: ZERO COMPANY, and all related names, characters, logos and game content are trademarks and/or
copyrighted works of Lucasfilm Ltd., Disney Enterprises, Inc., Electronic Arts Inc., Bit Reactor and/or their
respective licensors. "EA" is a trademark of Electronic Arts Inc. All other trademarks belong to their owners. They are
used here only to describe what this software works with (nominative use) and for no other purpose.

## No game content is included
This repository contains only original source code written for this project. It does **not** contain, and you must
**not** add to it or redistribute:

- game files, assets, textures, audio, portraits, text, or any data extracted from the game;
- the `gamedata/` folder produced by the optional extractor in `tools/extract` (it is git-ignored on purpose);
- save files (they contain your own progress and may embed game content);
- type-mapping (`.usmap`) files or the Oodle compression library, which are third-party works with their own terms.

The optional extractor only reads the installation **you** own, on **your** machine, and writes its output to a local
folder. You are responsible for making sure that your use of the game's files complies with the game's end-user license
agreement, the platform's terms of service, and the laws that apply to you. This is not legal advice.

## Use at your own risk
- The software is provided **"as is", without warranty of any kind**, express or implied (see `LICENSE`).
- Editing save files can corrupt them or change your game in ways the developers did not intend. Progress could be
  lost, and a game update can change the save format at any time. The editor makes backups and verifies every write,
  but that cannot be guaranteed to protect you in every situation. **Keep your own backups** of anything you care
  about and try edits on a copy first.
- The authors and contributors are not liable for any damage, data loss, account action or other consequence arising
  from the use or misuse of this software.
- Modifying a game may be restricted by its terms of service. If you play with online features, any changes to your
  save are at your own risk, and **this tool is intended for offline, single-player use only**. It must not be used to
  gain an unfair advantage over other players or to violate anyone's rights.

## What the tool does and does not do
- It reads and writes **save files only**. It does not inject into, attach to, or modify the running game or its
  memory, and it does not modify, patch or bypass the game's installation, DRM, licensing or anti-cheat systems.
- The local web server binds to `127.0.0.1` and is not meant to be exposed to a network.
- The web page loads Tailwind CSS and web fonts from public CDNs (Google Fonts), so your browser contacts those
  services when the page is opened. No save data is sent anywhere by this software.

## Rights holders
If you are a rights holder and believe something in this repository should not be here, please open an issue or contact
the repository owner and it will be reviewed and, where appropriate, removed promptly.
