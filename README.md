# NyaLZO

Pure-managed, **unsafe-free** .NET port of the LZO1X-1 algorithm (a port of
`linux/lib/lzo/lzo1x_compress.c`, v4.8) together with a MiniLZO-based decompressor.
No native dependencies.

- `NyaLZO.LZO1xCompressor.Compress(byte[] data)` — `byte[]` (raw LZO1X stream)
- `NyaLZO.LZO1xDecompressor.Decompress(byte[] data)` — `byte[]`

## Install

```
dotnet add package NyaLZO
```

## Usage

```csharp
using System.IO;
using NyaLZO;

byte[] data = File.ReadAllBytes("input.bin");

byte[] compressed = LZO1xCompressor.Compress(data);
File.WriteAllBytes("output.lzo", compressed);

byte[] restored = LZO1xDecompressor.Decompress(compressed);
```

## Limited support — please read

- Only the **LZO1X-1** variant is implemented (the common Linux-kernel variant).
  Other LZO variants (`lzo1x_999`, `lzo1`, `lzo2a`, …) are not available.
- This library works with the **raw LZO1X stream only**. The **lzop container**
  (magic `89 4C 5A 4F …`, lzop header, checksums, file name) is *not* implemented
  here — it is only usable for the raw block payload.
- Compression ratio and speed may differ from the reference C implementation.

## License

MIT
