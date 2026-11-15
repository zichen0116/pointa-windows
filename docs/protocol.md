# Pointa local protocol v1

Implementation: `src/Pointa.Server/HostService.cs`, `SrpPairing.cs`, `TrustStore.cs`; executable examples in `tests/Pointa.SelfTest/Program.cs`. The protocol is implemented for the Windows development build. The Android application must implement this exact contract before interoperability is claimed.

The in-process `PointerChanged` callback includes a monotonically increasing `Revision`, independent of the wire `seq`. Windows ignores older revisions after a hide/disconnect. `SlideRequested` carries the original session cancellation token; the UI must check it when dequeuing and immediately before native input. External callbacks run outside the service state lock.

## Transport and discovery

- TLS 1.2 or 1.3, `wss://<IPv4>:32145/ws`, UTF-8 JSON text messages only. No HTTP or plaintext WebSocket fallback. The server rejects WebSocket requests containing an `Origin` header; browser clients are outside this version.
- Windows 0.1.1 chooses explicit loopback/RFC1918 IPv4 addresses on active Wi-Fi and gateway-bearing Ethernet interfaces, prioritizing Wi-Fi. Windows Public/Private labels do not suppress LAN pairing. Kestrel never listens on wildcard/public IP addresses and rejects non-LAN source addresses. Address changes rebuild the listener; a ten-second check also catches interface changes. Optional user-invoked firewall setup opens only this executable's TCP 32145/UDP 32146 to LocalSubnet, following Windows administrator approval.
- UDP port **32146** accepts exactly the 19 ASCII bytes `POINTA_DISCOVER_V1\n` (one final LF), unicast or LAN broadcast. Response goes to the requester's source port. The UDP socket binds Any to receive broadcasts, but accepts only loopback/private source addresses on interfaces containing a caller-approved local address. It replies at most ten times/second globally. No token, code, or trusted certificate pin is advertised.

```json
{"v":1,"type":"pointa.discovery","serverId":"32-lowercase-hex","name":"电脑名称","port":32145,"pairingId":"32-lowercase-hex","expiresAt":1793800000000}
```

Discovery is untrusted: use the datagram's source IPv4 as the candidate host. `serverId` and `pairingId` are hints and must agree with the subsequent handshake. mDNS is not implemented. Manual IP plus code uses the same WSS/SRP path.

## Limits and timing

- Maximum **4096 bytes per complete WebSocket message**, including across fragments; maximum JSON nesting 8. Binary messages, duplicate top-level JSON keys, invalid field types and nonfinite coordinates are rejected.
- At most four pending authentications and sixteen TCP connections. At most thirty authentication starts per rolling sixty seconds globally. Six-digit-code pairing allows at most five SRP challenges per pairing epoch (180 seconds), whether the attempt reaches proof or not. Refreshing locally generates a new independent code and epoch. There is no network refresh endpoint.
- First authentication message within fifteen seconds; each following SRP message within fifteen seconds. Whole authentication deadline sixty seconds, with PC approval additionally limited to thirty seconds.
- An authenticated phone sends `heartbeat` at least once/second while idle. A controller with no valid message for more than three seconds is disconnected at the next one-second maintenance tick (maximum approximately four seconds); the pointer hides immediately when detected. Normal close, explicit disconnect and revocation hide immediately. This is failure detection, not instantaneous detection of an unplugged network.
- Controls: strictly increasing positive signed 64-bit `seq`, per connection; up to 240 messages in a one-second window. UTC Unix-millisecond `ts` must be no older than 2000 ms or more than 1000 ms ahead of the server. The phone estimates server clock offset from hello/heartbeat and uses its monotonic clock for elapsed intervals; never rely on phone/PC wall clocks already matching. Pointer sends should normally be limited to 60–120 Hz.
- Invalid control messages end the session with `{"v":1,"type":"error","code":"..."}` where possible. A new session starts at sequence 1 with a hidden pointer. Do not queue/replay controls across disconnects.

Server hello immediately follows the WSS handshake:

```json
{"v":1,"type":"hello","serverId":"...","name":"...","pairingId":"...","time":1793800000000,"heartbeatMs":1000,"maxMessageBytes":4096}
```

## QR pairing

The QR contains a URI, not an executable URL:

```text
pointa://pair?v=1&host=192.168.1.10&port=32145&id=<serverId>&pair=<pairingId>&pin=<certificateSha256>&token=<qrToken>
```

`serverId` and `pairingId` are lowercase 32-hex UUIDs without hyphens. `pin` is the lowercase SHA-256 hex digest of the **complete DER TLS leaf certificate** (64 characters, not an SPKI hash). `token` is 32 random bytes encoded as unpadded base64url (43 characters). Code and QR expire together after 180 seconds. Successful pairing rotates both; manual refresh immediately invalidates the previous epoch, including pending approvals. The QR chooses the first approved non-loopback address; other eligible addresses may be used if the selected address is unreachable and identity/pin remain unchanged.

Android must compare the actual leaf certificate digest to the scanned pin during TLS establishment, then check `hello.serverId`. Do not use a general trust-all TLS callback. Send:

```json
{"v":1,"type":"pair.qr","name":"我的手机","pairingId":"...","token":"..."}
```

Names are 1–64 characters after trimming, with no control characters. A valid secret reserves the epoch against competing pairings. The server sends `pair.pending` and asks for explicit PC approval. Denial returns `pairing_denied`; refresh is available on the PC to retry. After approval:

```json
{"v":1,"type":"pair.approved","deviceId":"32-lowercase-hex","credential":"43-base64url-characters","serverId":"...","certificateSha256":"..."}
{"v":1,"type":"session.ready","deviceId":"...","time":1793800000000,"effect":{"color":"#FF6475","size":16,"trailMs":500,"smoothing":"standard"},"display":{"width":2560,"height":1440}}
```

The independent 256-bit `credential` is returned once. Save it using Android Keystore-backed encryption, with host identity and certificate pin; exclude secrets from logs, backups and screenshots. Windows saves only SHA-256 of its UTF-8 text inside the protected trust store.

## Six-digit code: certificate-bound SRP-6a

No plaintext-code authentication and no fallback to it exist. This is **application-level SRP-6a over TLS**, not the TLS-SRP cipher suite and not a generic username/password login. Implementation uses **BouncyCastle.Cryptography 2.7.0**, `Srp6Server`, RFC 5054's 3072-bit group (`N` from Appendix A, `g = 5`) and SHA-256. The integration has not received independent cryptographic/security review and is not a release security certification.

When there is no trusted certificate yet, Android may establish an isolated candidate TLS connection solely for the SRP exchange. It must record the **actual peer TLS leaf DER digest** from that connection and must not send existing credentials, accept a new credential, or issue control before SRP mutual authentication. It must not globally weaken the HTTP client's trust policy. A certificate change on a known computer requires a new explicit pairing, never silent replacement.

Use the following identity bytes exactly, UTF-8, **no final newline**:

```text
pointa-srp-v1\n<serverId>\n<pairingId>\n<actual TLS leaf certificate SHA256 lowercase hex>
```

`\n` here denotes one LF byte. Server identity is constructed using its own stored certificate, never a client-supplied pin. Phone identity uses the actual certificate observed on its TLS connection. These fixed-length IDs plus explicit context and certificate binding prevent an attacker relaying SRP through a different TLS certificate and subsequently reading the credential. Password is exactly the six ASCII digits, including leading zeroes.

1. Phone sends:

```json
{"v":1,"type":"pair.srp.begin","name":"我的手机","pairingId":"..."}
```

2. Server sends fresh 16-byte random salt and its ephemeral public `B`:

```json
{"v":1,"type":"pair.srp.challenge","salt":"32-hex","b":"768-hex","group":"rfc5054-3072","hash":"SHA-256"}
```

3. Phone initializes Bouncy Castle `SRP6Client` with that group and SHA-256, generates `A` using the salt, bound identity and code, computes secret from `B`, then computes evidence `M1`:

```json
{"v":1,"type":"pair.srp.proof","a":"768-hex","m1":"64-hex"}
```

4. Server validates `0 < A < N`, computes the secret and verifies `M1`; then sends:

```json
{"v":1,"type":"pair.srp.server","m2":"64-hex"}
```

5. Phone must verify `M2` before computing the session key. Both peers obtain `K` using Bouncy Castle `CalculateSessionKey`. Phone sends:

```json
{"v":1,"type":"pair.srp.confirm","confirmation":"64-hex"}
```

`confirmation = HMAC-SHA256(Kbytes, UTF8("pointa-srp-confirm-v1\n" + pairingId))`, again no final newline. `Kbytes` is the unsigned big-endian session-key integer **left padded to 32 bytes**. The server validates the HMAC, reserves the still-current pairing epoch and only then sends `pair.pending` and asks PC approval. `pair.approved` and `session.ready` are identical to QR pairing. Persist the authenticated actual certificate pin.

Encoding: `A` and `B` are unsigned big-endian integers, exactly 384 bytes/768 lowercase hex digits, including leading zeroes. `M1`, `M2`, `K` are exactly 32 bytes/64 hex digits. Salt is exactly 16 bytes/32 hex digits. Server accepts hex case-insensitively but emits lowercase. Never use a signed `BigInteger` byte encoding.

Bouncy Castle's evidence convention is `M1 = H(PAD(A) || PAD(B) || PAD(S))`, `M2 = H(PAD(A) || PAD(M1) || PAD(S))`, `K = H(PAD(S))`, with `PAD` to group length (384 bytes), including the internal M1 operand in M2. Its `x = H(salt || H(identity || ':' || password))`, `k = H(PAD(N) || PAD(g))`, and `u = H(PAD(A) || PAD(B))`. Use the compatible BC Java SRP6 primitives or reproduce these exact formulas; some other SRP packages use different evidence conventions. Do not substitute an arbitrary “SRP” implementation.

Sources: [Bouncy Castle SRP6 server](https://github.com/bcgit/bc-csharp/blob/master/crypto/src/crypto/agreement/srp/SRP6Server.cs), [utilities/evidence](https://github.com/bcgit/bc-csharp/blob/master/crypto/src/crypto/agreement/srp/SRP6Utilities.cs), [RFC 5054 group definitions](https://www.rfc-editor.org/rfc/rfc5054#appendix-A). The pinned package and self-test are the concrete implementation contract.

## Reconnect and control

For a remembered computer, validate its stored certificate pin during TLS and check stable server ID. Then send:

```json
{"v":1,"type":"auth.resume","deviceId":"...","credential":"..."}
```

Success returns `session.ready` and does not ask PC approval again. Only one authenticated controller is allowed; a second receives `controller_busy` without displacing the first. A newly approved second phone may receive its credential before `controller_busy`; preserve the credential, then reconnect when control is released.

All following phone messages require `v`, strictly increasing `seq`, and corrected server-time `ts`:

```json
{"v":1,"type":"heartbeat","seq":1,"ts":1793800000000}
{"v":1,"type":"pointer.move","seq":2,"ts":1793800000016,"x":0.47,"y":0.62,"active":true}
{"v":1,"type":"pointer.hide","seq":3,"ts":1793800000032}
{"v":1,"type":"slide.next","seq":4,"ts":1793800000048,"id":"01234567-89ab-cdef-0123-456789abcdef"}
{"v":1,"type":"slide.prev","seq":5,"ts":1793800000064,"id":"11234567-89ab-cdef-0123-456789abcdef"}
{"v":1,"type":"effect.update","seq":6,"ts":1793800000080,"color":"#FF6475","size":16,"trailMs":500,"smoothing":"standard"}
{"v":1,"type":"device.forget","seq":7,"ts":1793800000096}
```

- Coordinates are finite doubles in `[0,1]`. `active` is required boolean. Latest pointer state replaces queued state; up to an 8 ms coalescing delay. Hide, touch-up (`active:false`), disconnect and revocation bypass that delay. Pointer control never maps to system mouse operations.
- Slide IDs are canonicalized UUIDs (hyphenated 36 characters). Deduplication survives reconnects for 120 seconds during the host process lifetime; stale timestamps independently prevent replay of older packets. IDs are not persisted over application restart. The client must create a new UUID for each deliberate press and **never retransmit a queued slide after reconnect**. Minimum slide cooldown 180 ms. Duplicate/cooldown rejections do not execute keys.
- Slide response is `{"v":1,"type":"action.result","id":"...","success":true,"error":null}`. Focus/application checks are the Windows adapter's responsibility; failure is returned as `success:false` with its explanatory error, and must be shown without inventing a current slide number.
- Effect color is `#RRGGBB`, size 6–40, trail 0–1200 milliseconds, smoothing `low|standard|high`. Updates persist and emit `effect.applied` with an `effect` object. Remote effect changes are limited to four/second; faster messages receive `effect.rejected`/`effect_cooldown` without disconnecting. UI should debounce settings changes.
- Windows 0.1.4 renders the upstream web Bézier trail. Default diameter is 12 DIP (Android small/medium/large = 8/12/20); standard geometry tension is 0.3 (low 0.15, high 0.45). Android 0.1.2 derives color from its theme and sends it on connection and theme changes; there is no independent color setting. The v1 fields and accepted ranges remain compatible.
- `session.ready` and `heartbeat.ack` include `display: {width, height}`, the selected laser monitor's positive integer pixel dimensions. Windows reads the real monitor bounds at startup, selection and display changes; the next one-second heartbeat reports the latest dimensions. `display` is `null` when unavailable. This additive v1 field can be absent on older hosts: clients must handle absent/null dimensions as unknown, never invent a physical monitor size. Pointer coordinates remain normalized `[0,1]` and independent of display origin. Client display-ratio fitting should update only after validated dimensions arrive.
- `heartbeat.ack` includes server `time`. `device.forget` revokes the authenticated device and ends its session. PC-side revoke/clear removes trust before interrupting current control. A rejected or revoked credential must lead to pairing, not unlimited automatic retry.

Common fatal errors: `authentication_required`, `invalid_credential`, `credential_revoked`, `controller_busy`, `pairing_expired`, `pairing_denied`, `invalid_pairing`, `invalid_proof`, `code_rate_limited`, `auth_rate_limited`, `unsupported_version`, `invalid_sequence`, `stale_message`, `rate_limited`, `invalid_pointer`, `invalid_effect`, `invalid_action_id`, `message_too_large`, `text_required`, `duplicate_property`, `unknown_message`. Missing/type-invalid fields use `invalid_<field>`. Timeouts may close the socket without a JSON error.

## Storage and implementation boundary

Windows atomically replaces a single CurrentUser DPAPI `trust.dat` blob containing server certificate/PFX, server ID, paired device metadata, SHA-256 credential hashes and effect preferences. Corruption/decryption failure stops loading and preserves the original file. The TLS private key is temporarily imported into the Windows user key container for SChannel compatibility; its saved PFX remains DPAPI protected. The certificate is not installed as a trusted root. No default telemetry, network credentials in discovery, firewall changes, or internet relay are involved.

Self-test runs only against loopback and uses temporary DPAPI data. It verifies real WSS handshakes including QR and SRP, incorrect TLS-certificate SRP binding, session/control validation, trust persistence, revocation and idle hiding. It does not replace independent protocol review, network packet capture, Android interoperability or hardware latency tests.

`tests/Pointa.InteropHost` is a separate loopback-only executable for the Android JVM client to exercise this production protocol. It has isolated temporary trust, auto-approves only its own test pairings, records pointer/effect/slide events and never calls native input. It is excluded from the Windows production executable and installer. Its stdin/stdout contract is documented in that directory; invitation output is test secret data and must not be committed.
