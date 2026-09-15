BBC Micro Cassette Loader
=========================

SPDX-License-Identifier: GPL-3.0-only
Copyright (C) 2026 Cliff Davies. Licensed under GPL-3.0-only.

This Windows application reads BBC Micro cassette recordings from WAV files or
line-in audio, validates cassette blocks with CRC checks, and exports recovered
files.
The Export menu can also create standard DFS 40/80-track SSD/DSD or classic
ADFS-S/M/L raw-sector disk images. The preflight allows format selection, disk-name
changes, and omission of files before writing.
Use File > New cassette... to safely clear the current recovery session; saved
recovery state is archived as a backup. Choose the WAV or line-in input separately.
File > Restore recovery backup... lists those timestamped backups and replaces the
current session only after confirmation. The current saved files are preserved in
a new safety backup, and diagnostic waveforms are not included in archived state.
If a WAV batch is imported while recovered data already exists, the application
asks whether to add it to the current cassette, start a new cassette, or cancel.

Requirements
------------

- Windows 7 SP1 or newer
- .NET Framework 4.7.2 or newer
- For line-in capture, a Windows audio input device

Running
-------

Run bbc-cassette-loader.exe. Use Import WAV to open one or more recordings, or
choose Input > Record line-in to WAV... to preserve a live 48 kHz mono capture
for later replay and recovery.
Recovered state is saved in an export folder beside the application when that
folder is writable. Protected installations use the per-user LocalAppData
fallback. Keep the whole application folder together; the DLL files and
bbc-cassette-loader.exe.config are required. The bundled help.html is available
from Help > Local help. Do not move or rename individual runtime files or the
help and licence files.

Help > Check for updates performs a manual, short-timeout request for the latest
stable release metadata from GitHub. It never downloads anything and sends no
cassette data, recovery state, file paths, or machine identifier. GitHub may see
the request's IP address, time, and application user-agent; the check can be
unavailable offline or when rate-limited.

The package contains no recordings or pre-existing recovery data. WAV files are
selected from their original locations through the application.

For optional support across RokCoder's software, games, and educational
projects, visit https://ko-fi.com/rokcoder. Support does not unlock features or
change the software licence.

Corresponding source
--------------------

The corresponding source for this release is available at no charge from the
public GitHub repository:
https://github.com/rokcoder-bbcmicro/bbc-cassette-loader

Use the source tree or source archive for the GitHub release tag matching this
package version. The repository contains the source needed to build the
application and the pinned third-party dependency references used by the
project.

This software is provided under the accompanying LICENSE. Third-party
component notices are in THIRD-PARTY-NOTICES.txt.
