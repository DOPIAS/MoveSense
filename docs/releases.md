# Releases

Run the **Windows** workflow from GitHub Actions on `main`. Set
`release_version` to the version to publish, such as `0.2.1`. Leave it empty
to run checks without publishing. Use a version that has not been released.

The workflow builds and tests the solution, then checks the portable app and
installer. It verifies a move and recovery, the Explorer registration, and
uninstallation. Only after these checks pass does it create the version tag
and publish a prerelease with:

- A self-contained Windows x64 portable ZIP.
- A per-user Windows installer.
- SHA-256 checksums for both downloads.

Version tags pushed separately also run the release job. GitHub supplies the
source archives. Build output, journals and test fixtures do not belong in Git.

Before each release, update `Directory.Build.props`, the default versions in
`scripts/build.ps1` and `installer/MoveSense.iss`, and `CHANGELOG.md`.
The workflow uses the changelog for the release description.

For a local build, run `./scripts/build.ps1 -Installer` on Windows with the
.NET 8 SDK and Inno Setup 6. The script writes versioned files to `artifacts/`
and refuses to replace an existing portable directory or ZIP.

## Explorer setup from the command line

`MoveSense.exe --enable-context-menu` registers the current copy for files and
folders. `--disable-context-menu` removes it. `--context-menu-status` exits with
0 when both registrations point to the current executable, 1 otherwise, or 2
when the check fails. Registration uses the current user's registry hive.
