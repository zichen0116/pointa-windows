# Session service implementation plan

This module implements the PRD section 6, independent of WinForms. Its callers are the Windows UI and the console self-test. Core contains only UI-neutral models and validation. The server owns TLS, pairing, protected trust storage, discovery and one controller; UI owns approval, rendering and presentation focus checks.

1. Persist one self-signed RSA certificate, stable host ID, credential hashes and visual preferences in an atomically replaced DPAPI CurrentUser blob. Refuse corrupt data; never silently reset trust.
2. Listen only on caller-approved explicit IPv4 loopback/RFC1918 addresses. TLS 1.2/1.3, WSS /ws. Limit frames, pending authentication and rates.
3. QR: 256-bit random single-use secret, cert pin, host/port, 120-second expiry. Code: BouncyCastle SRP-6a, RFC5054 3072-bit group, SHA-256, identity bound to actual TLS leaf certificate. No plaintext-code fallback. Both require explicit PC approval before issuing a per-device 256-bit credential; save only its hash.
4. One authenticated controller. Validate protocol version, monotonic sequence, fresh server-clock timestamps, normalized coordinates and effect bounds. Deduplicate action UUIDs across reconnects. Coalesce pointer state; prioritize hide and disconnect. Idle heartbeat timeout removes control.
5. UDP discovery supplies no secrets and never establishes trust. mDNS is deferred. Protocol and a real pinned WSS integration test cover QR, code, bad proof, certificate binding, expiry, replay, revocation and malformed input.

Safety boundary: commands cannot move/click the OS pointer; slide keys are delivered only by the Windows presentation adapter. This application-level SRP integration is not TLS-SRP and has not received independent security review.
