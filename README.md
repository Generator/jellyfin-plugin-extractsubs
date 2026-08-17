<h1 align="center">Better Subtitle Extractor (Jellyfin Plugin)</h1>
<h3 align="center">Fork of the <a href="https://jellyfin.org">Jellyfin Project</a> subtitle extract plugin</h3>

## About

Plugin to automatically extract embedded subtitles and attachments with powerful filters.

## Features

- **Smart filtering** — extract only the languages and codec types you want, include or exclude SDH and forced streams, and accept/reject streams by title with regex patterns.
- **Format control** — keep subtitles in their native format or convert text subtitles to SRT for maximum compatibility.
- **Jellyfin-friendly output** — files follow Jellyfin's naming convention (language + `.default` / `.forced` / `.sdh` markers) so they're picked up
  automatically, and can be written safely even when a file already exists
- **Library selection** — restrict extraction to specific libraries (empty means all)

## How it works

Probes your media files directly and extracts embedded subtitle streams to
external files next to the media, following Jellyfin's naming convention
(`basename.lang.ext`, with `.default` / `.forced` / `.sdh` markers where
applicable) so they are picked up automatically. Extraction writes to a
temporary file and publishes it atomically, so a failed or interrupted run never
leaves a partial subtitle behind.

## Installation

[See the official documentation for install instructions](https://jellyfin.org/docs/general/server/plugins/index.html#installing).

## Build

1. To build this plugin you will need [.NET 10.x](https://dotnet.microsoft.com/download/dotnet/10.0).

2. Build plugin with following command
  ```
  dotnet publish --configuration Release --output bin
  ```

3. Place the dll-file in the `plugins/Better Subtitle Extractor_<version>` folder (you might need to create the folders) of your JF install, alongside a `meta.json` manifest.

## Acknowledgments
- [jellyfin/jellyfin-plugin-subtitleextract](https://github.com/jellyfin/jellyfin-plugin-subtitleextract) for the original Jellyfin plugin.
- [alchemyyy/jellyfin-plugin-subtitleextract](https://github.com/alchemyyy/jellyfin-plugin-subtitleextract) for the additional plugin filters and regex.

## Licence

This plugins code and packages are distributed under the MIT License. See [LICENSE](./LICENSE) for more information.
