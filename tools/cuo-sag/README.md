# cuo-sag — UO-Sagas `.sag` support for the ClassicUO DLLs

CentrED loads all UO client assets through the prebuilt DLLs in `lib/dotnet`
(`ClassicUO.Assets/IO/Renderer/Utility`), built from mainline
[ClassicUO](https://github.com/ClassicUO/ClassicUO) at the commit pinned in
`build.ps1` (currently `48ba0f5c`). This kit patches that source so the DLLs
transparently read UO-Sagas encrypted `.sag` asset files, then rebuilds them.

## What the patch does

- **`patches/SagCrypto.cs`** (new file, ported from the UO-Sagas game client
  `UOFileSag.cs`/`AES256_CTR.cs`): decrypts both `.sag` formats, auto-detected
  per file —
  1. **AES-256-CTR** (52-byte trailer: size, SHA-256 key-recognition digest,
     nonce; key compiled in), and
  2. **legacy AES-256-CBC** (56-byte header: key, IV, size — key stored in the
     file).
  Decryption is **eager** (no lazy page decryption — the editor touches most
  assets anyway). Also exposes `GetPlaintextSize()` used by CentrED's client
  version detection.
- **`patches/sag-support.patch`**:
  - `MMFileReader` decrypts any file with a `.sag` extension right after
    memory-mapping it and exposes the plaintext, so every loader parses it
    exactly like a `.mul`. A `.sag` that matches neither format throws
    `InvalidDataException`.
  - `UOFileManager.GetUOFilePath` falls back from a missing `x.mul` to an
    existing `x.sag` (plain `.mul` always wins when both exist; non-`.mul`
    names like `multi.idx` are never remapped — the converter leaves those
    unencrypted).
  - `FileReader.Length` became a settable property so decrypted files report
    their plaintext length.

## Rebuilding the DLLs

```powershell
powershell -ExecutionPolicy Bypass -File tools\cuo-sag\build.ps1
```

Clones ClassicUO into `tools/cuo-sag/.work` (gitignored), checks out the
pinned commit, applies the patch, builds, and copies the four DLLs into
`lib/dotnet`. Use `-SkipPatch` to produce pristine upstream DLLs (parity
testing).

## Key rotation

The AES-256-CTR key in `patches/SagCrypto.cs` (`AesCtrKey`) **must match the
key compiled into the UO-Sagas game client**
(`UO-Sagas-ModernUO-Client/src/ClassicUO.IO/UOFileSag.cs`). If the client
rotates or adds keys, mirror the change here (`SelectKey` supports checking
multiple candidate keys) and rebuild. The legacy CBC scheme carries its key in
the file header and needs no coordination.

## Bumping the ClassicUO version

When updating `lib/dotnet` to a newer ClassicUO commit: change `$CuoCommit` in
`build.ps1`, run the build, and fix up `patches/sag-support.patch` if the
patched files drifted (re-apply by hand in `.work`, then regenerate with
`git -C tools/cuo-sag/.work diff > tools/cuo-sag/patches/sag-support.patch`).
