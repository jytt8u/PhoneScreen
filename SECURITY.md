# Security

## Supported versions

Fixes go into the latest release. PhoneScreen 1.1 pins the official scrcpy 4.1 Windows x64 build; updating any bundled component means updating the pinned hashes in the source.

## How the connection is protected

- **TLS only.** PhoneScreen puts a small relay between its ADB server and the phone. The relay forwards ADB's opening `CNXN` packet and only continues if the phone answers with `STLS` and ADB agrees to switch. Anything else, including a plaintext `AUTH` reply, closes the connection before keys, commands or media are sent. The check happens on the same socket the stream uses, so there is no gap between "checked" and "used".
- **Local addresses only.** Phone addresses must be numeric private IPv4 addresses (10/8, 172.16/12, 192.168/16). Port 5555, the legacy unencrypted ADB port, is refused, and `adb tcpip` is never issued.
- **Loopback listeners.** The relay accepts exactly one connection on 127.0.0.1. PhoneScreen's own ADB server binds to a random loopback port and is shut down when the app closes; a server started by Android Studio or another tool is left alone.
- **Discovery is read-only.** The phone list comes from mDNS (`adb mdns services`) with automatic connection disabled. Discovered names and addresses are validated before use, and only private addresses are accepted.
- **Pairing secrets.** The six-digit code and the QR password are passed to ADB on standard input, never on the command line, never logged and never saved. Each QR code is random (from the system CSPRNG) and is replaced after one attempt.
- **Untrusted device text.** The phone's model name is filtered to letters, digits and a few punctuation marks before it is shown or used as the window title, so it can't inject command-line options.
- **No idle sharing.** Clipboard synchronisation is off unless you enable it. Nothing is recorded, and there's no telemetry, account or relay server.

## The runtime

- Downloads come from a fixed GitHub release URL over HTTPS, follow redirects only to GitHub's own hosts, have a size limit and must match a pinned SHA-256. Partial downloads are never installed.
- ZIP entries are checked so they can't escape the install directory; symbolic links and other reparse points are refused.
- Before each start, every runtime file is hashed against a manifest embedded in `PhoneScreen.exe`. Unknown files (for example a planted DLL) are rejected. The verified files stay open without write or delete sharing while PhoneScreen runs.
- Environment variables that could redirect ADB or scrcpy (`ADB_VENDOR_KEYS`, `ANDROID_SERIAL`, `SCRCPY_SERVER_PATH` and others) are cleared for child processes.
- The `.data` folder with your settings is restricted to your Windows account and SYSTEM.

## Known limits

- Wireless debugging gives the paired PC full ADB access to the phone. Use it on networks and computers you trust, turn it off when you're done, and remove PCs you no longer use under *Paired devices*.
- ADB's key lives in `%USERPROFILE%\.android` and is shared with other Android tools. PhoneScreen doesn't isolate it.
- While a phone is connected, any program running on the same PC can talk to the local ADB server, as with any ADB setup. The random port makes this harder to stumble on, but it is not an access control.
- A compromised Windows account, administrator, phone or router is outside what PhoneScreen can protect against.
- `PhoneScreen.exe` is not code-signed, so SmartScreen may warn on first launch. Release checksums are in `SHA256SUMS.txt`.
- Apps can block screen or sound capture, and wireless latency depends on the phone and the network.

## Reporting a problem

Please use **Security → Report a vulnerability** on GitHub. If that isn't available, open an issue with a short description and steps to reproduce. Leave out pairing codes, private keys, your addresses and full logs, and don't post working exploits against other people's devices.
