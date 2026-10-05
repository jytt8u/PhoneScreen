# Changelog

## 1.1.1 — 2026-10-05

Fixes for phones that wouldn’t connect. Tested with a Samsung Galaxy A51 on Android 13.

- Samsung phones leave `service.adb.tls.port` empty, and PhoneScreen refused to connect because of it. TLS is now checked on the connection itself, which was already the real safeguard.
- Phone search uses adb’s own mDNS backend. The Openscreen backend that 1.1.0 forced didn’t see phones on some networks.
- If the saved address doesn’t answer (the phone got a new IP or port), PhoneScreen searches again and connects to the new address by itself.
- Phone search restarts when nothing is found for a while, so a phone that turns on Wireless debugging later still shows up.
- Clear messages for “Wireless debugging is off”, “not on the same Wi-Fi” and “this PC isn’t paired any more”, detected right away instead of after a 15-second wait.
- Fixed a “pipe closed” error when adb exited before reading its input.

## 1.1.0 — 2026-10-04

### Connecting
- Phones with Wireless debugging on are found on the network automatically. No more copying IP addresses and ports.
- Pair by scanning a QR code; PhoneScreen pairs and connects in one go.
- The pairing-code address fills itself in, so only the six digits need typing.
- Follows Android's port changes and picks the right phone when several are around.
- Waits for the phone to finish authorising before talking to it, which fixes intermittent "device offline" failures right after connecting.
- TCP keep-alive on the phone link, and automatic reconnects (up to three) when Wi-Fi drops mid-session.

### Sound
- A failed audio capture no longer kills the whole stream; you keep the picture and get a clear message.
- Automatic fallback from Opus to AAC on phones without an Opus encoder.
- Audio buffers tuned per profile for Wi-Fi, which removes most crackling.
- "PC and phone" now always uses playback capture as scrcpy requires; on Android 12 and older it quietly falls back to PC-only.

### Interface
- New layout in English with the phone's name and status, a dark title bar and dark scroll bars.
- Profiles: Balanced, Responsive, Weak Wi-Fi and Movies.
- New options: share clipboard, keep window on top.
- Proper scaling with Windows display zoom.
- The phone window gets the PhoneScreen icon and the phone's model as its title.
- Enter connects, or pairs while the cursor is in the pairing fields.

### Other
- Runtime downloads accept TLS 1.3.
- Settings from 1.0 are migrated.

## 1.0.0 — 2026-10-04

- Android screen, mouse and keyboard input, and phone audio over Wi-Fi.
- Portable release with scrcpy 4.1 and a one-click runtime installer.
- TLS required on the live connection, loopback-only listeners, embedded runtime hashes and file locks.
