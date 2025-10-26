SPEC v1.2

# Clip Manager
is a lightweight Windows clipboard manager inspired by CopyClip for macOS. It lives in the Windows system tray, captures clipboard text updates, persists history across restarts, and delivers quick access to stored snippets.

### Target runtime
• NET 10 LTS (released October 15, 2025) on Windows 11+.

### OUT OF SCOPE
• Handling images, file drops, or other binary clipboard formats in v1.
• Cloud sync, telemetry, or remote storage.

### Goals
• Provide passive capture of textual clipboard content, quick recall, clearing, and truncation awareness.
• Persist clipboard history locally while remaining resource-efficient and unobtrusive. Offer keyboard-friendly interaction from the system tray.
• The app operates entirely offline.
• Distribution via MSI installer (generated post-tag by external process; unsigned).
• Clipboard listener processes text-oriented formats and stores their payloads exactly as provided. Images/files are ignored.
• Maximum stored entry length is limited by SQLite’s default row length (1,000,000,000 bytes). Content that would exceed this limit is truncated and surfaces a notification.
 
 ## High-Level Architecture

• Tray Host (WPF): Single-instance process managing NotifyIcon, message pump, DI container, and UI shells.
• Clipboard Listener Service: Hidden window registered with AddClipboardFormatListener, extracting raw textual payloads.
• Persistence Layer: SQLite database (Microsoft.Data.Sqlite) stored under %AppData%/ClipManager for entries and settings.
• Settings Manager: SQLite-backed key/value store for history depth, retention, startup toggle, hotkeys, ignore list, notification preferences.
• UI Layer: Tray context menu, settings dialog, Windows toast notifications for truncation/errors.
• Maintenance Services: Background jobs for pruning, vacuuming, integrity checks, and backup rotation.
 
### Component Interactions
 1. Tray Host initializes services, loads persisted history, and starts Clipboard Listener.
 2. Clipboard Listener receives WM_CLIPBOARDUPDATE, gathers raw text payload, applies, SQLite length guard (truncate if needed), and passes the entry to History Manager.
 3. History Manager dedupes via content hash, persists asynchronously to SQLite, updates observable collections, and triggers truncation toasts when applicable.
 4. UI binds to History Manager for live updates; user commands (re-copy, delete, clear) route back through the manager. Settings changes persist immediately and adjust listener/manager behavior (limits, ignore list, hotkeys).

 ### Data Model
 ClipboardEntries: Id INTEGER PK, CreatedUtc, TextContent (raw payload), SourceApp, Hash.
 Settings: Key TEXT PK, Value TEXT/JSON.
 Indexes on CreatedUtc DESC, and Hash support ordering/deduplication.
 
### Clipboard Handling
• Hidden STA window prevents self-trigger loops by tracking app-originated copies.
• Debounce interval (default 250 ms) avoids flooding; ignore list filters sensitive processes.
• Truncation leverages SQLite row limit, marking entries with IsTruncated and recording original length before notifying the user.

### UI/UX
• Tray right-click: Show Settings: 
    - recent snippets (top N, showing format badges and truncation indicator), Clear History, Preferences, Quit.
        - row click -> copy to clipboard
    - global hotkey (default Ctrl+Shift+V): history window with search, timestamped list full keyboard navigation.
    - launch on startup toggle
    -settings
        - clear history
        - adjust number of stored entries
    - quit functionality
• Settings dialog: history depth, retention days, in-memory cache size, format priority, max text length (up to SQLite limit), hotkey editor, ignore list, notification toggles, theme (light/dark/system).
• Notifications: Windows toast for truncation events.

### Persistence & Startup
• Async write queue with WAL mode ensures responsiveness; periodic JSON backups (retain latest N) stored in %AppData%/ClipManager/backups.
• Autostart toggle manipulates registry Run key or Startup folder shortcut.
• Clear History wipes everything and resets cache.

### Error Handling & Logging
• Logging via Microsoft.Extensions.Logging with rolling files under %LocalAppData%/ClipManager/logs.
• Clipboard read failures retried with exponential backoff; persistent issues raise toast and disable listener until reset.
• SQLite integrity check at launch; corruption triggers restore from latest backup and user alert.

### Security & Privacy
• Entirely offline; data stored under user profile with appropriate ACLs.
• Regex-based exclusion filters prevent saving sensitive strings
• Truncation notifications inform users when content was shortened to respect storage limits.

## Testing Strategy
• Unit tests: History Manager (dedupe/truncation), Format Router logic, Settings persistence, debounce behavior.
• Integration tests: simulated clipboard events covering plain/HTML/RTF sequences through persistence, truncation boundary cases.
• UI automation: smoke tests for tray menu, history popup, truncation notification workflow via WinAppDriver/Playwright for Windows.
• Manual QA: MSI install/uninstall, autostart toggle, rapid text capture across formats, truncation toasts, ignore list validation, theme switching.

### Implementation Roadmap
1. Project Setup: WPF app targeting .NET 10 with single-instance guard, DI, logging.
2. Persistence Layer: SQLite schema/migrations, async repository, WAL configuration, backup routines.
3. Clipboard Listener & Format Router: Win32 interop capturing prioritized text formats, truncation enforcement, ignore list support.
4. History Manager: In-memory cache, dedupe, truncation notifications, command API.
5. Tray & UI: NotifyIcon, tray menu, format/truncation indicators, settings dialog.
6. Settings & Startup: Preference storage, validation, global hotkey registration, autostart toggle.
7. Stabilization: Logging polish, error dialogs, panic clear, backups, accessibility/theming.
8. Packaging: Define MSI packaging (WiX/VS Installer) for local deployment.

### Risks & Mitigations
• Global hotkey conflicts: validate availability before saving; prompt user for alternatives.
• Unsigned MSI warnings: communicate expected security prompts within installer UX/documentation.