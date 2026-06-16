# Building LazyPainter

1. Install the .NET SDK.
2. Set a `KSP_ROOT` environment variable to your KSP folder (the one with `KSP_x64.exe`).
3. Build: open `LazyPainter.slnx`, or run `dotnet build LazyPainter.slnx`.

Other ways to set the game path: https://kspbuildtools.readthedocs.io/en/stable/msbuild/ksp-install.html

# Releasing LazyPainter

1. Follow steps 1 and 2 from above.
2. Run `.\release.ps1`.