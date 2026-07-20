# Quartz 2.0 Windows release

- Installer: `Quartz-2.0.0-Setup.exe`
- Release date: July 20, 2026
- Supported system: 64-bit Windows 10 or Windows 11
- Installer size: 196,518,866 bytes (187.4 MiB)
- SHA-256: `26A0A8BE82A055A2A95801CC80BDF6208657B3D71C69C3A0BBF6AA0A2A793702`

This installer contains a self-contained .NET 8 publish, CefSharp/Chromium runtime binaries, CEF resources and locales, and Microsoft's signed Visual C++ 2015-2022 x64 prerequisite. It does not install or use WebView2.

The installer and application are currently unsigned. Public production distribution should use a trusted Windows code-signing certificate.

Release highlights and extension limitations are maintained in [`../update.md`](../update.md) and [`../README.md`](../README.md).
