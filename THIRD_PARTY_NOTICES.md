# Third-party components

The project license is recorded in [LICENSE](LICENSE). The following runtime and development components retain their own licenses.

- **.NET 8 / Windows Desktop Runtime**: Microsoft and .NET contributors. Framework-dependent builds require a separately installed runtime. Self-contained builds also redistribute runtime files and their notices. See [dotnet/runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) and [dotnet/winforms](https://github.com/dotnet/winforms/blob/main/LICENSE.TXT).
- **Microsoft.Windows.SDK.NET.Ref / Windows SDK projections**: the Windows targeting pack supplies Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll in the published application. Its package metadata links to the [Windows SDK license](https://aka.ms/WinSDKLicenseURL). These components retain their own licenses.
- **@napi-rs/canvas**: optional icon-generation development tool; not needed to compile or run the app and not bundled with the executable. See the [component repository](https://github.com/Brooooooklyn/canvas). Icon drawing commands are in `tools/Draw-Icon.cjs` in the source package.
