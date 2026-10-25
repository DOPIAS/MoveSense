# Safety and recovery

Analysis reads supported files in the source and the selected scan folders.
The desktop UI includes the containing folder by default, shows its path and
provides a checkbox to opt out. Drive roots are never implicitly included.
Dropping a file only selects it; choosing a destination starts a read-only check.
It makes no repairs until the user reviews and confirms a plan.
Unselected folders, application databases, unknown binary formats, registry
values and arbitrary program arguments are outside the analysis.

| Format | Detection | Automatic repair |
| --- | --- | --- |
| Markdown | Inline links/images and reference definitions; code samples skipped | Exact destination token |
| JSON / JSONC | String values that look like local paths | Exact string token |
| Project XML | Selected quoted Include, Update, Project and HintPath paths | No |
| XML / INI / TOML / YAML | Selected quoted path/file/folder/source/target values | No |
| Windows .lnk | Target and working directory through Windows Script Host | No |

Relative paths are interpreted from the directory containing their document. Some applications use a different base; review their configuration paths carefully.

This is selected syntax support, not a complete parser for every language.
Globs, environment variables, MSBuild expressions and URLs are skipped.
Markdown extensions, unusual escaping and embedded HTML may not be recognized.

Reference analysis excludes `.git`, `.svn`, `node_modules`, `bin`, `obj`,
`.vs` and `.local`. Files above 4 MiB produce visible warnings.
Scans stop above 50,000 files. Source snapshots are limited to 50,000 entries.
All moved source files are hashed, including those in excluded folders.

## Execution

Only local moves within one drive root are supported. The destination must
not exist and its parent must exist. Drive roots, moves into themselves,
network shares, symbolic links and junctions are blocked. Case-only renames
are not supported.

MoveSense checks source hashes, analyzed reference hashes and the scanned
file inventory again before mutation. Changed plans require new analysis.
Hashing large sources can take time.

Original reference bytes are written and flushed before the move. The durable
journal records expected file hashes and edits. Documents are written through
neighboring temporary files and replaced atomically. Cancellation is deferred
after execution starts.

The move and all repairs are not one filesystem transaction. Locks, permission
errors, concurrent writes or a crash may interrupt them. MoveSense records a
recoverable state. Checks cannot eliminate every race with another application;
close editors or sync tools actively changing the selected files.

Review-only references and scan warnings require explicit acknowledgment.
No detected problems does not prove that no outside reference exists.

## Recovery

Choose Put it back immediately after a move, or select an operation in History
and choose Undo / recover. Both actions use the same checked recovery process. Recovery accepts
original or repaired reference hashes, so interrupted repairs or recoveries
can be resumed. It refuses a new destination occupant, recreated source,
added/removed files or directories, damaged backups and newer edits.
It never deletes added files to force rollback.

Journals and original reference bytes live under
`%LOCALAPPDATA%\MoveSense\operations`. Keep them together. SQLite is a
rebuildable history index.

Only repaired reference files are backed up. Moved data is not copied.
Deleted or corrupted moved data cannot be reconstructed. Recovery restores
reference contents and locations; it is not a full backup of every filesystem
attribute, alternate stream or application state. Uninstall preserves history.


