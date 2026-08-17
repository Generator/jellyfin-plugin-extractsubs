<h1 align="center">Better Subtitle Extractor for Jellyfin Plugin</h1>
<h3 align="center">Fork of the <a href="https://jellyfin.org">Jellyfin Project</a> subtitle extract plugin</h3>

## About

Plugin to automatically extract embedded subtitles with powerful filters.

## Features

- **Smart filtering** — extract only the languages and codec types you want, include or exclude SDH and forced streams, and accept/reject streams by title with regex patterns.
- **Format control** — keep subtitles in their native format or convert text subtitles to SRT for maximum compatibility.
- **Jellyfin-friendly output** — files follow Jellyfin's naming convention (language + `.default` / `.forced` / `.sdh` markers) so they're picked up
  automatically, and can be written safely even when a file already exists

## Installation

[See the official documentation for install instructions](https://jellyfin.org/docs/general/server/plugins/index.html#installing).

## Build

1. To build this plugin you will need [.Net 6.x](https://dotnet.microsoft.com/download/dotnet/6.0).

2. Build plugin with following command
  ```
  dotnet publish --configuration Release --output bin
  ```

3. Place the dll-file in the `plugins/subtitleextract` folder (you might need to create the folders) of your JF install

## Acknowledgments
- [jellyfin/jellyfin-plugin-subtitleextract](https://github.com/jellyfin/jellyfin-plugin-subtitleextract) for the original Jellyfin plugin.
- [alchemyyy/jellyfin-plugin-subtitleextract](https://github.com/alchemyyy/jellyfin-plugin-subtitleextract) for the additional plugin filters and regex.

## Licence

This plugins code and packages are distributed under the MIT License. See [LICENSE](./LICENSE.md) for more information.
