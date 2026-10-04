# Security

## Supported release

Security fixes target the latest PhoneScreen release. Version 1.0.0 pins the official scrcpy 4.1 Windows x64 distribution. Dependencies are checked against that distribution, not automatically replaced with unrelated binaries.

## Connection and local files

- The transport bridge requires the ADB `CNXN → STLS → STLS` exchange on the actual connection before allowing encrypted traffic. Official ADB performs TLS authentication and encryption. A separate preliminary probe does not authorize a later connection.
- Phone addresses must be numeric private IPv4 addresses. Conventional legacy port 5555 is rejected. No `adb tcpip` command is issued.
- The bridge accepts one loopback client. The separate ADB server binds to loopback on a random port, without network discovery or automatic device connection.
- Pairing codes enter ADB through standard input, are masked in error output and cleared from the interface. PhoneScreen does not save them.
- Downloads use a fixed release URL, HTTPS-only redirects to permitted GitHub hosts, a size limit, cancellation and a pinned SHA-256. Partial downloads are not installed.
- Archive paths cannot escape the staging directory. Existing reparse points are rejected. Extraction and hashing use the same locked archive handle.
- Runtime files are compared with an embedded hash manifest before execution. Unexpected files are rejected; expected files stay open without write/delete sharing for the session. A local marker file cannot bypass verification.
- Downloaded runtime directories and application settings are restricted to the current Windows user and SYSTEM.
- Automatic clipboard synchronisation is disabled. Explicit paste can still send text. PhoneScreen does not record the screen or audio and has no account, telemetry or cloud relay.

ADB uses the standard Windows user identity in `%USERPROFILE%\.android`. That identity can be shared by other Android development tools. PhoneScreen runs a separate server but does not isolate the cryptographic identity. Removing the paired computer on Android revokes that computer's ADB access; disconnecting in PhoneScreen only ends the current session.

## Review and validation

The release review addressed plaintext downgrade and probe/connection races, mutable verification markers, archive path traversal, inherited command overrides, unsafe shell arguments, runtime replacement during a session, unbounded download/output handling, and shutdown/download races.

Local validation passed 66 checks with the official runtime: malformed addresses and packets, a real loopback transport exchange, plaintext rejection, file tampering, forged markers, unexpected DLLs, file write locks, actual ADB startup and loopback binding. A separate live download/install check passed. CI runs the offline regression suite.

These checks do not establish that every third-party component is free of vulnerabilities. The bundled ADB reports 37.0.0; dependencies should be reviewed again when updating the pinned distribution. Physical-phone pairing, image quality, input and audio playback have not yet been verified on hardware.

## Limits

Wireless debugging grants broader ADB access than screen viewing. Use a trusted computer and local network, disable wireless debugging after use, and remove the paired computer when access is no longer needed. A compromised Windows account, administrator, compromised phone or malicious router is outside the launcher's protection boundary.

The executable is not code-signed. Protected Android content and apps that prohibit audio capture may not stream. Wi-Fi latency depends on the device and network.

## Reporting

Use the repository's **Security → Report a vulnerability** form when available. If it is unavailable, open an issue containing a short description and reproduction steps without pairing codes, private keys, personal addresses or sensitive logs. Do not include a working exploit against another person's device.
