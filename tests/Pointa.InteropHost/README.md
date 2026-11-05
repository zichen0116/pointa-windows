# Windows / JVM interoperability host

This executable uses the production `HostService`, DPAPI trust store, TLS certificate, SRP implementation and control guards. Its only adapter is a test event recorder. It **never calls native input**. It always binds `127.0.0.1`, disables discovery, uses a fresh temporary DPAPI directory and deletes that directory on normal exit. Pairing is automatically approved only inside this test program. Never use its invitations with production devices or retain them in logs.

Build from the Windows repository with `.\..\.tools\dotnet\dotnet.exe build tests/Pointa.InteropHost -c Release`. Run `dotnet tests/Pointa.InteropHost/bin/Release/net10.0/Pointa.InteropHost.dll --port 32155` (port is optional). Send UTF-8 JSON lines over stdin. EOF or `quit` disposes the service. Abrupt process termination can leave its temporary test directory until system cleanup.

Stdout is newline-delimited JSON; ignore events other than the one awaited. The `ready` event contains `qr`, `code`, `certificateSha256`, `serverId`, `pairingId`, `port`, `host`, `expiresAt`. It is sensitive test fixture data. Do not copy stdout to committed files. Later `fixture` events have the same fields. State/pointer events can arrive before or between command replies.

| stdin JSON | Result |
| --- | --- |
| `{"command":"fixture"}` | Current invitation as `event:"fixture"` |
| `{"command":"refresh"}` | Rotate invitation, then emit fixture |
| `{"command":"display","width":2560,"height":1440}` | Set simulated display metadata for the test |
| `{"command":"display"}` | Clear display; protocol sends `null` |
| `{"command":"disconnect"}` | Disconnect controller and hide pointer |
| `{"command":"revoke","deviceId":"…"}` | Revoke trust and disconnect matching controller |
| `{"command":"state"}` | `event:"snapshot"` with connectedDeviceId, devices, pointer, slides, display, effect |
| `{"command":"quit"}` | Dispose and exit normally |

Each successful command ends with `{"event":"command","command":"…"}`. Invalid input emits `command.error`. Other events: `pointer` with `x/y/active/revision`; `slide` with `direction:"next"|"prev"` and cumulative `count`; `effect` with `effect`; `state` with `connectedDeviceId`; `approval` with `deviceName/method`. Display is initially `null`: only explicit test commands install simulated dimensions.

The Android JVM interoperability test should launch this DLL using the local .NET runtime, consume `ready`, drive the real WSS client, and use stdin/snapshots to assert revocation and pointer hiding. This test harness must not be shipped in production installers.
