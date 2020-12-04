# Zero Install Publishing Tools

[![Build status](https://img.shields.io/appveyor/ci/0install/0publish-win.svg)](https://ci.appveyor.com/project/0install/0publish-win)  
The Zero Install Publishing Tools provide a graphical editor and wizard for creating [Zero Install feeds](https://0install.github.io/docs/packaging/). They also contain an alternative version of the [0publish command-line tool](https://docs.0install.net/tools/0publish/) optimized for use on Windows. These tools are based on [Zero Install .NET](https://github.com/0install/0install-dotnet).

**[Documentation and download instructions](https://docs.0install.net/tools/0publish-win/)**

## Building

The source code is in [`src/`](src/) and generated artifacts are placed in `artifacts/`.  
The source code does not contain version numbers. Instead the version is determined during CI using [GitVersion](http://gitversion.readthedocs.io/).

To build install [Visual Studio 2019 v16.8 or newer](https://www.visualstudio.com/downloads/) and run `.\build.ps1`.  

## Contributing

We welcome contributions to this project such as bug reports, recommendations, pull requests and [translations](https://www.transifex.com/eicher/0install-win/). If you have any questions feel free to pitch in on our [friendly mailing list](https://0install.net/support.html#lists).

This repository contains an [EditorConfig](http://editorconfig.org/) file. Please make sure to use an editor that supports it to ensure consistent code style, file encoding, etc.. For full tooling support for all style and naming conventions consider using JetBrains' [ReSharper](https://www.jetbrains.com/resharper/) or [Rider](https://www.jetbrains.com/rider/) products.
