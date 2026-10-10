# Development

You need the .NET 10 SDK.

```
dotnet build
dotnet test
dotnet run --project src/ZeroCompany.App
```

Publish a self-contained single file for one platform:

```
dotnet publish src/ZeroCompany.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/publish/win-x64
```

(`linux-x64`, `osx-arm64` and `osx-x64` work the same way.) Pushing a `v*` tag runs `.github/workflows/release.yml`, which
builds and uploads all four.

## Layout
```
src/ZeroCompany.Core/      GVAS reader/patcher, save container, editable model, actions, apply/verify/backup service, view models
src/ZeroCompany.GameData/  game-data extractor (CUE4Parse) and the typed game database
src/ZeroCompany.App/       Avalonia desktop UI (MVVM): screens, theme, portraits
tests/ZeroCompany.Tests/       core, game-data and service tests
tests/ZeroCompany.App.Tests/   headless UI tests that also render screenshots
tools/golden/              scripts that produce the comparison files described below
```

## Tests
Tests that need a real save, extracted game data or the game install skip themselves when those are absent (they are never
committed), so a fresh clone runs the synthetic tests and the UI smoke tests only. To run everything:

- put a save in `saves/` (tests only ever write to temporary copies);
- extract game data once, or keep the `gamedata/*.json` dumps there;
- for the extractor test, set `ZC_GAME_DIR`, `ZC_USMAP` and `ZC_OODLE_DIR` (or leave a `.usmap` and `oodle-data-shared.dll` in
  `gamedata/`) so the test never downloads anything.

**Parity with the original Python editor.** The suite compares the C# code with the v0.3.0 Python editor on your sample save:
identical fields and screen views, and byte-identical files for scripted edits. The comparison files are produced from the
`v0.3.0` tag; see [`tools/golden`](../tools/golden/README.md). They contain game text and save contents, so they are git-ignored.

**UI screenshots.** The UI tests render screens to `artifacts/shots/`. Set `ZC_UPDATE_DOCS=1` and run the
`Regenerate_readme_screenshots` test to refresh `docs/screenshots/` (portraits are switched off so no game artwork is shown).
Set `ZC_PORTRAITS=off` to run the app itself with initials instead of portraits.

## Third-party software
[Avalonia](https://avaloniaui.net/) (MIT), [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) (MIT),
[Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache-2.0, decodes the OpenEXR portraits in saves),
[CUE4Parse](https://github.com/FabianFG/CUE4Parse) (Apache-2.0, used by the extractor), which downloads Oodle (proprietary,
Epic Games Tools / RAD) at run time, and a community-made `.usmap` mappings file you obtain yourself. Tests: xUnit
(Apache-2.0). The UI uses the Barlow Condensed font family (SIL OFL) when it is installed.

## Acknowledgements
The earlier [Zero_Company_Save_Editor](https://github.com/jpatrickp512/Zero_Company_Save_Editor) by jpatrickp512 was a useful
reference for which save properties matter; no code was copied from it. The earlier Python/web implementation (v0.1.0 to
v0.3.0) is kept in git history under those tags.
