Smoother streaming on everyday Wi-Fi, and one less click.

Download **PhoneScreen-1.2.0-win64.zip**, extract it and run **PhoneScreen.exe**. scrcpy is included. Requires Windows 10/11 x64 and Android 11 or newer.

### New

- **Auto profile.** While connecting, PhoneScreen pings the phone and picks resolution, bit rate and buffering for your link. Fast Wi-Fi gets the lowest delay; Wi-Fi with spikes gets a little smoothing so the picture doesn't stutter and the sound doesn't crackle. It's the new default.
- **Connects by itself** when the phone you used last shows up on the network.
- **The phone stays awake** during a session and goes back to its usual screen timeout afterwards.

### Check your download

These files were built by GitHub Actions from the `v1.2.0` source, not on a personal PC. To confirm, install the [GitHub CLI](https://cli.github.com/) and run:

```
gh attestation verify PhoneScreen-1.2.0-win64.zip -R jytt8u/PhoneScreen
```

Or skip the download entirely and [build it from source](https://github.com/jytt8u/PhoneScreen#building-from-source).
