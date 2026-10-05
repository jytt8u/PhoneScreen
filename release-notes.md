Fixes for phones that wouldn't connect. If 1.1.0 kept saying it couldn't find or connect to your phone, this is the update.

Download **PhoneScreen-1.1.1-win64.zip**, extract it and run **PhoneScreen.exe**. scrcpy is included. Requires Windows 10/11 x64 and Android 11 or newer.

### Fixed

- **Samsung phones connect again.** One UI doesn't publish the ADB TLS port property, and PhoneScreen used to stop there. Encryption is still enforced on the connection itself.
- **Your phone shows up in the list.** Phone search now uses adb's own mDNS backend, which finds phones on networks where the previous one didn't.
- **New address? No problem.** When the phone gets a new IP or port, PhoneScreen notices, searches again and connects to the new one.
- **Better messages.** "Wireless debugging is off", "not on the same Wi-Fi" and "pair again" are told apart, immediately.

### Check your download

These files were built by GitHub Actions from the `v1.1.1` source, not on a personal PC. To confirm, install the [GitHub CLI](https://cli.github.com/) and run:

```
gh attestation verify PhoneScreen-1.1.1-win64.zip -R jytt8u/PhoneScreen
```

Or skip the download entirely and [build it from source](https://github.com/jytt8u/PhoneScreen#building-from-source).
