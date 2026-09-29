# p1z.subtitler

A tiny Windows tray utility for typing directly onto your own desktop in real time.

## Download

Grab `p1z.subtitler-win-x64.zip` from the [latest release](https://github.com/P1-Z/p1z.subtitler/releases/latest), unzip it anywhere, and run `p1z.subtitler.exe`. It is a single self-contained exe; nothing else needs installing.

Windows SmartScreen may warn that the app is unrecognised because it is not code-signed. Choose **More info → Run anyway**.

## Run

Launch `p1z.subtitler.exe`. The app lives in the system tray and has no required main window.

Default hotkey: **Ctrl + Alt + S**

1. Press the hotkey from any application. Nothing appears yet.
2. Type. The black rectangle fades in with your first character and each one after it appears immediately as the subtitle itself; there is no input box and no cursor.
3. Backspace edits the live subtitle.
4. Press Enter or Escape to remove it and return to the previous application.

While subtitle mode is active, the overlay captures keyboard input so the application underneath does not receive it. Enter and Escape return the keyboard to the previous application. The borderless overlay stays on top and is constrained to the active monitor's Windows work area.

## The box

The subtitle is a semi-transparent black rectangle with white text in it. No border, no rounded corners, no cursor. The only treatment on it is the CRT surface described below.

Nothing is drawn while the subtitle is empty. The rectangle fades in with the first character, and fades out again if you delete every character or end the session with Enter or Escape. The overlay still holds the keyboard for the whole session, whether or not anything is visible.

Text is centred and wraps at word boundaries when a line runs out of room. A word too long for one line is broken across lines rather than clipped. If the whole subtitle would not fit the monitor, the font size is reduced until it does.

The box is centred on the position chosen for the session and grows outwards as you type; it never moves while the text sits still.

## Settings

Right-click the tray icon and choose **Settings**. You can configure the hotkey, font, font size, box opacity, maximum shake, placement range, CRT look, rainbow words, and optional Windows startup.

Settings are stored at:

`%LOCALAPPDATA%\P1Z\Subtitler\settings.json`

The default maximum shake is 3 px. Larger values read more like bouncing than shaking, especially at large font sizes.

## CRT look

The subtitle is drawn as if it were sitting on a CRT: dark scanlines every three pixels, a bright band rolling slowly down the box, a faint mains flicker, a phosphor glow around every glyph, and the red/cyan fringing of three misconverged guns.

**CRT LOOK** sets the strength of the whole treatment as a percentage. The default is 55. Set it to 0 and the subtitle is plain white text on a flat black rectangle.

The effect is painted over the box and reserves no space of its own, so turning it up never changes where the subtitle sits or how large it is. It keeps running through the fade out, and stops repainting once the box is gone.

## Rainbow words

Words on the **RAINBOW WORDS** list render as an animated rainbow gradient that sweeps across the whole word and keeps cycling while it is on screen. The default list is `gay`.

Matching is case-insensitive and whole-word only: `GAY` lights up, `gaydar` does not. Separate entries with commas or spaces. Clearing the field switches the effect off. Rainbow and shaking are independent, so an all-caps trigger word does both.

## Caps / shaking

Shake strength updates live from the percentage of alphabetic characters that are uppercase. Punctuation, spaces, and numbers do not affect the ratio. Lowercase text remains stable; partial caps wobble mildly; full caps use the configured maximum. Removing uppercase text eases the shake back down.

Each uppercase character rides its own continuous motion curve at roughly 7-12 Hz rather than jumping to a fresh random offset every frame. That is fast enough to read as a vibration rather than a bounce, while a 60 Hz frame still lands several samples per cycle, so nothing snaps. The motion never repeats visibly and does not reset when you type or delete another character; amplitude eases in and out instead of popping.

The box stays anchored. Shaking is rendered per character inside it, within space the box always reserves, so the box itself never grows or twitches. Set **MAX SHAKE** to 0 to switch the effect off entirely.

## Bundled font

The default subtitle face is **Press Start 2P**, bundled with the application under the SIL Open Font License 1.1. The distributed `OFL.txt` contains the license. Other installed fonts remain selectable in Settings.

## Requirements and limitations

- Windows 10/11 x64.
- The configured hotkey must not already be registered by another program.
- Some exclusive fullscreen games can render above normal desktop topmost windows; borderless/windowed applications work normally.
- Startup registration is per-user and does not require administrator rights.

## Build

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

The standalone build is written to `dist`.
