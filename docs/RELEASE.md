# Releasing VL.Mapsui

The steps to the first release on nuget.org, what each one proves, and what is still the
maintainer's to decide. Written 2026-09-26, the day after VL.NetTopologySuite went first, and
modelled on its [docs/RELEASE.md](https://github.com/rednotfound/VL.NetTopologySuite/blob/main/docs/RELEASE.md),
which holds the fuller account of the publishing methods checked against nuget.org's own
documentation.

**Nothing here is done automatically, and nothing is pushed without the maintainer saying so.** A
version on nuget.org can be unlisted, never deleted or replaced.

Order across the family: **VL.NetTopologySuite ✅ (2026-09-26) → VL.Mapsui → VL.Overworld**, with
VL.GeoJSON free to go any time. This package declares `VL.NetTopologySuite 0.0.1-alpha`, which
resolves from nuget.org since 2026-09-26 — before that, publishing this one would have produced a
package nobody could install.

---

## Where it stands (2026-09-26)

| step | proves | state |
|---|---|---|
| `dotnet test` | the arithmetic, and that nothing rebuilds per frame | ✅ 244 tests |
| `tools\Test-VLPackage.ps1` | the package can contribute nodes at all, no stray tiles, every help patch pinned `0.0.0` | ✅ |
| `tools\Test-VLPatch.ps1` | every `.vl` well formed, `Help.xml` complete, every node F1-flagged | ✅ 20 documents, 32 of 32 |
| `pack.ps1` + `tools\Compile-HelpPatches.ps1` | every node in every help patch resolves against the **packed** nupkg, and every process node is built in `Create` — read from the generated C# | ✅ 19 patches |
| **`tools\Test-Install.ps1 -FromNuGetOrg`** | `nuget install` brings all six dependencies **from nuget.org itself**, and every help patch **inside the installed package** compiles with that install as the only repository | ✅ 2026-09-26: VL.NetTopologySuite 0.0.1-alpha arrived carrying nuget.org's `.signature.p7s`; 19 of 19 compile. **Without the switch the same run also passed — on a local build** (see below) |
| the vvvv GUI | every help patch opens, draws, and reads without clipped notes | ✅ 2026-09-25, all 19 photographed maximized |
| the maintainer's own review in vvvv | the patches read right to someone who uses them | ✅ 2026-09-25, all 19 hand-arranged |
| publish to nuget.org | a stranger's vvvv can install it | ✅ **2026-09-26, `0.0.1-alpha`**, browser upload by the maintainer (way A) |
| install back from nuget.org alone | nuget.org behaves like the local feed | ✅ 2026-09-26: the published nupkg diffed against `dist\feed` — identical but for `.signature.p7s`; `Test-Install.ps1 -Published -Version 0.0.1-alpha` installed VL.Mapsui **and** VL.NetTopologySuite from nuget.org, both signed, 34 packages, 19 of 19 help patches compile from the install |
| tag `v0.0.1-alpha` on GitHub | the source matching the package is findable | ⬜ the maintainer: on `3a1ac54`, the commit the uploaded nupkg was packed from |
| GitHub release on the tag | release notes readable without the nuspec | ⬜ the maintainer, in the browser |
| working version bumped to `0.0.2-alpha` | a local repack can never pass for the published package | ✅ 2026-09-26: nuspec, `LayerNodes.UserAgent`, and the VL.NetTopologySuite dependency (its working version) |

`VL.Mapsui` did not exist on nuget.org before the upload (flat container 404, checked 2026-09-26);
the upload created it under the maintainer's account.

**A green install test can come from your own disk.** The first run on 2026-09-26, without
`-FromNuGetOrg`, reported VL.NetTopologySuite 0.0.1-alpha "came along" — and it had, from
`%USERPROFILE%\.nuget\packages`, whose `.nupkg.metadata` named the sibling's `dist\feed`: a local
build packed the night before the real upload. Its DLL was byte-identical to nuget.org's; the file
differed only by nuget.org's repository signature, which is exactly why nothing could tell them
apart. `-FromNuGetOrg` drops the sibling feed, reads neither cache, and fails any `VL.*` dependency
without `.signature.p7s`.

---

## Versioning

- **Every release is a prerelease for now**, `0.0.1-alpha` first — the same suffix as the family,
  managed in step with VL.NetTopologySuite. A user installs with `nuget install VL.Mapsui -pre`.
- **The version is written in three places, and nothing overrides them:** the nuspec's
  `<version>`, `LayerNodes.UserAgent` (what OSM's tile servers see, since their policy asks to
  identify the application), and — from the second release on — the nuspec's dependency on
  `VL.NetTopologySuite`, which follows that package's version.
- **Right after publishing, the working version becomes `0.0.2-alpha`,** with a comment in the
  nuspec saying so, as VL.NetTopologySuite's already does. The dev loop repacks the same version all
  day, and NuGet uses any cached copy whose version matches without looking at the feed.
- The help patches pin `Version="0.0.0"` and never change (`tools\Normalize-HelpPatches.ps1`).

## Settled for this release

- **The description opens with `EARLY - not ready for real work yet.`**, the line VL.NetTopologySuite
  settled on: true, and a prerelease page should say so.
- **`<readme>docs\README.md</readme>`** — nuget.org renders the README on the package page, and the
  upload's Verify step previews it. Relative links in it (`docs/...`) do not resolve there; the same
  is true of VL.NetTopologySuite's page and was accepted.
- **The package ships README, ARCHITECTURE and MAPSUI-SURFACE, not NOTES.md.** NOTES is the
  repository's working log — two thousand lines of forensics and one machine path.
- **`LICENSE`** at the root (MIT, VL.Mapsui Contributors) matches the nuspec's licence expression.
- **The credit is on the map in every patch that shows tiles, and no node is needed for it.**
  OSM's policy: "Show OpenStreetMap licence attribution clearly on the map"; OpenTopoMap is
  CC-BY-SA. Mapsui's renderer prints each layer's attribution bottom right by itself — measured
  2026-09-26 (`AttributionRenderingFacts`) after the user saw it with no widget. The `Attribution`
  node, which only drew a second copy, was removed the same day (33 → 32 nodes).
- The repository is public, as are the other three and vvvv-gis (checked 2026-09-25).

## How to publish — way A for this release

nuget.org's account page labels API keys "Not Recommended" since a policy change announced
2026-08-03: new keys live at most **30 days**, and **every key created before 2026-08-17 stops
working on 2026-11-01** — [.NET Blog, *Strengthening NuGet Supply Chain Security: Reducing API
Key Lifetime*](https://devblogs.microsoft.com/dotnet/strengthening-nuget-supply-chain-security-reducing-api-key-lifetime/),
checked 2026-09-26. The same post points CI at Trusted Publishing (OIDC, launched September 2025).

- **A. Upload in the browser — no key at all.** nuget.org → *Upload* →
  `dist\feed\VL.Mapsui.0.0.1-alpha.nupkg` → the **Verify** page shows every nuspec field, the six
  dependencies and a **Preview** of the README → anything wrong: fix, repack, upload again →
  **Submit**, the irreversible click. VL.NetTopologySuite went this way on 2026-09-26: validated
  and indexed within about ten minutes, confirmed by email, nothing needed a retry.
- **B. A 30-day API key and `nuget push`** — still works, but a key per release month is the chore
  the next option removes. Fallback only.
- **C. Trusted Publishing from GitHub Actions** — an OIDC token traded for a one-hour key, nothing
  stored. The shape for `0.0.2-alpha` across the family, one policy per package on nuget.org. A
  GitHub runner has no vvvv, so the gate below stays local and must be green before the tag.

---

## The steps, in order

Every step separate; a validator never runs in the same command as a commit or a push.

```powershell
# 0. vvvv closed, tree clean except what this release changes.
git status

# 1. The gate, from a clean build:
.\build.ps1
dotnet test test\VL.Mapsui.Tests\VL.Mapsui.Tests.csproj
.\pack.ps1 -NoBuild
.\tools\Test-VLPackage.ps1
.\tools\Test-VLPatch.ps1
.\tools\Compile-HelpPatches.ps1
.\tools\Test-Install.ps1 -FromNuGetOrg -OutputDirectory <an empty folder>

# 2. The maintainer uploads dist\feed\VL.Mapsui.0.0.1-alpha.nupkg (way A) and checks the Verify
#    page: six dependencies incl. VL.NetTopologySuite 0.0.1-alpha, the README preview. Submit.

# 3. After the confirmation email, install back from nuget.org alone into a fresh folder and
#    compile the shipped help patches from it:
.\tools\Test-Install.ps1 -Published -Version 0.0.1-alpha
#    then, then in vvvv: nuget install VL.Mapsui -pre,
#    Help Browser -> VL.Mapsui -> HowTo Show a map, switch Enabled on.

# 4. Tag the commit the package was packed from, and push it.
git tag v0.0.1-alpha
git push origin v0.0.1-alpha

# 5. GitHub release on the tag, body = the nuspec's release notes (the maintainer, in the browser).

# 6. The same day: nuspec <version> and LayerNodes.UserAgent -> 0.0.2-alpha, release notes gain a
#    "0.0.2-alpha - In development, not published." line; build, test, commit.
```

## After the first release

- VL.Overworld can declare VL.Mapsui like any other package instead of reaching it through
  `--package-repositories`.
- The next release goes out with way C, written once for the family.
