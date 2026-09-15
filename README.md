# BBC Micro Cassette Loader

Recover files from standard 1200-baud BBC Micro cassette recordings.

## Download

Download the latest Windows ZIP from the [GitHub releases page](https://github.com/rokcoder-bbcmicro/bbc-cassette-loader/releases).
Extract the complete ZIP before running `bbc-cassette-loader.exe`.

## Requirements

- Windows 7 SP1 or newer
- .NET Framework 4.7.2 or newer
- A Windows audio input device for live line-in use

## Quick start

1. Run `bbc-cassette-loader.exe`.
2. Choose **Import WAV...** and select one or more cassette recordings.
3. Let the application combine valid blocks from the recordings.
4. Save the recovery state or export any complete recovered files.

For a difficult recording, open **Import options** and enable **Use recovery passes**.
This takes longer but can recover blocks that the ordinary pass misses. The
advanced CRC-repair option is available as a final, conservative fallback.

## Use line-in

Click **Line-in options ▾** to choose one of the two line-in actions:

- **Listen to line-in** — decode and monitor the live input.
- **Record line-in to WAV...** — decode while saving a WAV that can be replayed
  later with the ordinary or recovery-pass importer.

While line-in is listening or recording, the button changes to **Stop line-in**.

## Export recovered files

Use **Export** to choose one of these outputs:

- **Recovered files and .inf...** — payload files with BBC metadata.
- **CSW cassette image...** — a CSW cassette image.
- **BBC Micro disk image...** — DFS SSD/DSD or classic ADFS S/M/L images.

Only complete recovered files are exported. Existing files are not overwritten.

## Notes

- The application targets standard 1200-baud cassette files. 300-baud tapes
  and custom commercial loaders are outside the current scope.
- No sample recordings or pre-existing recovery data are included. Select WAV
  files from their original locations or create one with line-in recording.
- **Help → Check for updates** checks GitHub for a newer stable release. It does
  not download or install anything.

## Source and licence

The corresponding source is available in this repository at the matching
release tag. The project is licensed under
[GPL-3.0-only](LICENSE); third-party notices are in
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
