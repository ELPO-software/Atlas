# Third-party notices

Atlas is distributed under the [MIT license](LICENSE). It ships with the components below, each under its own license. The full texts are in the [licenses](licenses) folder, copied as their authors publish them. They are also built into Atlas: the settings show each one, so that they travel with every copy, including the single executable.

| Component | Version | License | Copyright | Full text |
|---|---|---|---|---|
| .NET runtime | 10.0.14 | MIT | .NET Foundation and Contributors | [dotnet.txt](licenses/dotnet.txt), [notices](licenses/dotnet-third-party-notices.txt) |
| Microsoft.Data.Sqlite, Microsoft.Extensions | 10.0.14 | MIT | .NET Foundation and Contributors | [dotnet.txt](licenses/dotnet.txt), [notices](licenses/dotnet-third-party-notices.txt) |
| Avalonia | 12.1.5 | MIT | AvaloniaUI OÜ | [Avalonia.txt](licenses/Avalonia.txt) |
| ANGLE, as packaged by Avalonia | 2.1.27548 | BSD-3-Clause | The ANGLE Project Authors | [ANGLE.txt](licenses/ANGLE.txt) |
| Inter font, as packaged by Avalonia | | SIL Open Font License 1.1 | The Inter Project Authors | [Inter.txt](licenses/Inter.txt) |
| SkiaSharp | 3.119.4 | MIT | Xamarin, Inc. and Microsoft Corporation | [SkiaSharp.txt](licenses/SkiaSharp.txt), [notices](licenses/SkiaSharp-third-party-notices.txt) |
| HarfBuzzSharp | 8.3.1.3 | MIT | Xamarin, Inc. and Microsoft Corporation | Same as SkiaSharp |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | .NET Foundation and Contributors | [CommunityToolkit.Mvvm.txt](licenses/CommunityToolkit.Mvvm.txt), [notices](licenses/CommunityToolkit.Mvvm-third-party-notices.txt) |
| MicroCom.Runtime | 0.11.6 | MIT | Nikita Tsukanov | [MicroCom.Runtime.txt](licenses/MicroCom.Runtime.txt) |
| Tmds.DBus.Protocol | 0.94.1 | MIT | Alp Toker and other contributors | [Tmds.DBus.Protocol.txt](licenses/Tmds.DBus.Protocol.txt) |
| Serilog, Serilog.Sinks.File, Serilog.Extensions.Logging | 4.4.0, 7.0.0, 10.0.0 | Apache-2.0 | Serilog Contributors | [Serilog.txt](licenses/Serilog.txt) |
| SQLitePCLRaw | 2.1.13 | Apache-2.0 | SourceGear, LLC | [SQLitePCLRaw.txt](licenses/SQLitePCLRaw.txt) |
| SQLite, built into SQLitePCLRaw | | Public domain | | [SQLite.txt](licenses/SQLite.txt) |

The notices files list the components that those libraries themselves include, such as Skia and HarfBuzz in SkiaSharp.

## SQLite

The authors of SQLite have dedicated it to the public domain. In place of a legal notice, they offer this blessing:

> May you do good and not evil.
> May you find forgiveness for yourself and forgive others.
> May you share freely, never taking more than you give.

## Keeping this file current

The list of components in the settings of Atlas comes from `ThirdPartyComponents`, and a test fails when a package that ships with Atlas is missing from it. `ThirdPartyNoticesTests` checks that each of those components appears here and that every license file this page links to exists. When a package changes, update the version above and copy its license as the package publishes it.
