# Guide

PhoneScreen shows your Android phone in a window on Windows. Click to tap, drag to swipe, scroll with the mouse wheel and type on your keyboard. Phone sound plays through the PC.

You need Windows 10 or 11 (x64) and Android 11 or newer, both on the same network. The PC can be on a cable to the same router. Nothing has to be installed on the phone.

## First time

**1. Get PhoneScreen.** Download the ZIP from [Releases](https://github.com/jytt8u/PhoneScreen/releases/latest), extract it somewhere you can write to (Desktop or Documents is fine, Program Files is not) and run `PhoneScreen.exe`. If you only downloaded the EXE, click **Install engine** and it fetches scrcpy for you.

**2. Turn on Wireless debugging.** If you've never used Developer options: Settings → About phone → tap *Build number* seven times. Then go to Settings → System → Developer options (the location varies by brand) and turn on **Wireless debugging**. Accept the prompt about the network.

**3. Pair.** In Wireless debugging, tap **Pair device with QR code** and point the camera at the code in PhoneScreen. Pairing takes a couple of seconds, then the phone screen opens on its own.

If Windows asks whether *adb* may use the network, allow it on private networks. That's how PhoneScreen finds the phone.

### Pairing with a code instead

Tap **Pair device with pairing code** on the phone. PhoneScreen fills in the address from the popup, you type the six digits and press **Pair**. If the address doesn't appear, copy it from the popup (the IP and the port shown there).

## Every time after that

Turn on Wireless debugging and open PhoneScreen. The phone shows up under **Connect your phone** within a few seconds; press **Connect** or Enter.

Android picks a new port every time Wireless debugging restarts. PhoneScreen follows that automatically, so you don't need to retype anything. If you have several phones, use **Choose phone**.

To stop, close the phone window or press **Disconnect**.

## Stream settings

Click **Change** next to *Stream settings*. Your choices are remembered.

| Setting | What it does |
| :--- | :--- |
| Balanced | 1600 px, 60 fps, 8 Mb/s. A good default. |
| Responsive | 1280 px, 60 fps, short audio buffer. The least delay, for games and quick tapping. |
| Weak Wi-Fi | 1024 px, 30 fps, 3 Mb/s and a longer audio buffer. For busy or distant Wi-Fi. |
| Movies | 1920 px, 12 Mb/s, picture and sound both buffered by 150 ms so they stay in sync. |
| Sound: On this PC | Phone sound plays on the PC and the phone goes quiet. |
| Sound: PC and phone | Plays on both. Android 13+, and some apps don't allow it. |
| Audio codec | Opus sounds best. AAC works on more phones. PhoneScreen switches by itself if Opus fails. |
| Control | Use the mouse and keyboard on the phone. Turn it off for view-only. |
| Hardware keyboard | Types in whatever language the phone's physical keyboard layout is set to. Press `Alt+K` in the phone window to pick a layout. |
| Turn the phone screen off | The phone display goes dark while you work from the PC; mirroring keeps going. |
| Share clipboard | Copy on one device, paste on the other. Off by default. |
| Keep window on top | The phone window stays above other windows. |

Handy shortcuts in the phone window: right-click is Back, middle-click is Home, `Alt+F` is fullscreen, `Alt+O` turns the phone screen off and `Alt+Shift+O` turns it back on.

## When something goes wrong

**The phone isn't found.** Check that Wireless debugging is on and that both devices are on the same Wi-Fi. Guest networks, "AP isolation" on the router and VPNs block devices from seeing each other. You can always type the address from the Wireless debugging screen (IP and port, e.g. `192.168.1.24:37001`).

**"The phone didn't accept this PC".** The phone doesn't know this PC any more, usually because it was removed from *Paired devices*. Pair again.

**Pairing fails.** The QR code and the six-digit code can each be used once. Scan the new QR code PhoneScreen shows, or open a fresh pairing code on the phone.

**No sound.** On Android 11 the phone has to be unlocked when you connect. Some apps (banking, some streaming services) block sound capture. If it still doesn't work, set the audio codec to AAC.

**Sound crackles or the picture stutters.** Try the Weak Wi-Fi profile, move closer to the router, or use the 5 GHz network. The Movies profile also smooths out short hiccups.

**Mouse clicks don't work on a Xiaomi phone.** Enable *USB debugging (Security settings)* in Developer options.

**Anything else.** Click **Show log** in the top right. The last lines usually say what happened.

## When you're done

Turn off Wireless debugging on the phone. Pairing gives the PC full ADB access, so if you stop using PhoneScreen on a computer, remove it under Wireless debugging → *Paired devices*. ADB keeps its key in your Windows profile (`%USERPROFILE%\.android`), shared with other Android tools such as Android Studio.

PhoneScreen doesn't record the screen or sound and doesn't send anything outside your network. See [SECURITY.md](SECURITY.md) for the details.
