# Contributing

Open an issue for a new reference format or a change to move semantics first.
Include a small synthetic example of the files and paths involved.

Use Windows and the .NET 8 SDK:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release --no-build
dotnet run --project src/MoveSense.Desktop
```

If VSTest's loopback connection is blocked, run the same test methods directly
with `dotnet run --project tests/MoveSense.Core.Tests`. CI uses VSTest.

Keep detectors separate from the UI. Implement `IReferenceDetector` and inject
it into `Scanner`. Extensions are compiled code; there is no runtime plugin
loader. Test incoming and outgoing links, Unicode, stale plans and recovery
before enabling automatic repairs.

Use exact token edits. Preserve text encoding, BOM and line endings.
Review-only analysis is preferable to an unreliable automatic edit.
Do not weaken safety checks to make a test pass.

The desktop uses WPF. Keep filesystem operations in the core library so they
can be tested independently of the UI.

Use focused commits with clear messages.
Never include local journals, build outputs, credentials or personal fixtures.
