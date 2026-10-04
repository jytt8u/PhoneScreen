# Third-party components

PhoneScreen's launcher source is MIT-licensed. The portable release includes the complete, unmodified official [scrcpy 4.1 Windows x64 archive](https://github.com/Genymobile/scrcpy/releases/tag/v4.1).

| Component | Bundled version | License | Source |
| :--- | :--- | :--- | :--- |
| scrcpy client and Android server | 4.1 | Apache-2.0 | [Genymobile/scrcpy v4.1](https://github.com/Genymobile/scrcpy/tree/v4.1) |
| Android Debug Bridge | 37.0.0-14910828 | Apache-2.0 | [Android platform tools](https://android.googlesource.com/platform/packages/modules/adb/) |
| FFmpeg shared libraries | 8.1.2 | LGPL-2.1-or-later, as distributed by scrcpy | [FFmpeg 8.1.2 source](https://ffmpeg.org/releases/ffmpeg-8.1.2.tar.xz) |
| SDL | 3.4.12 | zlib | [SDL release-3.4.12](https://github.com/libsdl-org/SDL/tree/release-3.4.12) |
| libusb | 1.0.30 | LGPL-2.1-or-later | [libusb 1.0.30](https://github.com/libusb/libusb/releases/tag/v1.0.30) |

License texts are in `licenses/`, and the original scrcpy license is also retained in `tools/scrcpy/`. PhoneScreen launches the upstream tools as separate processes; the launcher does not statically link these libraries. The distributed DLLs remain replaceable, although the launcher deliberately accepts only the pinned, verified distribution. A modified launcher can be built from source to change that policy.

The PhoneScreen release also provides the FFmpeg and libusb source archives corresponding to these versions. Build scripts and configuration used by the upstream Windows distribution are in [scrcpy's app/deps directory at v4.1](https://github.com/Genymobile/scrcpy/tree/v4.1/app/deps). Those source archives are separate downloads, not required to run PhoneScreen. `SHA256SUMS.txt` covers the release files.

Upstream authors retain their copyrights. PhoneScreen is not affiliated with or endorsed by Genymobile, Google, FFmpeg, SDL or libusb.
