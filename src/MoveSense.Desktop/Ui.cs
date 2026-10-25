using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace MoveSense.Desktop;

internal static class Ui
{
    public static string Language { get; private set; } = "en";
    private static readonly Dictionary<string, (string En, string Ru)> Texts = new()
    {
        ["MoveNav"] = ("Move files", "Перенести"),
        ["HistoryNav"] = ("History", "История"),
        ["SettingsNav"] = ("Settings", "Настройки"),
        ["LocalOnly"] = ("LOCAL FILES. YOUR CONTROL.", "ФАЙЛЫ ПОД ВАШИМ КОНТРОЛЕМ."),
        ["ScopeNote"] = ("Only selected folders are scanned. Nothing is uploaded.", "Анализируются выбранные папки. Данные не отправляются."),
        ["Heading"] = ("Where should it go?", "Куда переносим?"),
        ["Subtitle"] = ("Drop a file or folder here, then choose where to put it. We’ll check the links before moving.", "Перетащите сюда файл или папку и выберите, куда перенести. Перед переносом проверим ссылки."),
        ["Source"] = ("File or folder", "Файл или папка"),
        ["Destination"] = ("Choose a destination folder", "Выберите папку назначения"),
        ["ScanScope"] = ("Also inspect", "Также проверить"),
        ["ScopeHelp"] = ("The source is always scanned. Add a workspace to find incoming links. The new full path includes the final file or folder name.", "Исходная папка проверяется всегда. Укажите рабочую папку для поиска входящих ссылок. Новый путь включает конечное имя."),
        ["File"] = ("Choose file…", "Выбрать файл…"),
        ["Folder"] = ("Choose folder…", "Выбрать папку…"),
        ["Browse"] = ("Browse…", "Выбрать…"),
        ["Cancel"] = ("Cancel", "Отмена"),
        ["Analyze"] = ("Check & continue", "Проверить и продолжить"),
        ["EmptySummary"] = ("Analysis never changes your files.", "Анализ не изменяет файлы."),
        ["References"] = ("References", "Ссылки"),
        ["ScanNotes"] = ("Scan notes", "Замечания"),
        ["EmptyTitle"] = ("A clear view of what changes", "Все изменения перед вами"),
        ["EmptyBody"] = ("Choose a file or folder and its new path. MoveSense will trace local references within your chosen scope.", "Укажите исходный и новый путь. MoveSense найдёт локальные ссылки в выбранной области."),
        ["Repair"] = ("Apply the previewed Markdown and JSON repairs", "Применить показанные исправления Markdown и JSON"),
        ["Acknowledge"] = ("I reviewed the scan notes and accept unresolved references", "Замечания проверены, неисправленные ссылки учтены"),
        ["MoveAction"] = ("Move here", "Перенести сюда"),
        ["SafetyFootnote"] = ("No overwrites. Original reference files are saved before repairs.", "Без перезаписи файлов. Оригиналы документов сохраняются до исправлений."),
        ["HistoryHelp"] = ("Undo checks every moved file and repaired document. Recovery stops if newer edits or added files would be affected.", "Откат проверяет все перенесённые файлы и исправленные документы. Новые файлы и правки блокируют восстановление."),
        ["Refresh"] = ("Refresh", "Обновить"),
        ["Undo"] = ("Undo / recover selected", "Откатить / восстановить"),
        ["Appearance"] = ("Appearance", "Оформление"),
        ["Language"] = ("Language", "Язык"),
        ["ExplorerHeading"] = ("File Explorer", "Проводник"),
        ["ExplorerHelp"] = ("Right-click a file or folder on the desktop or in Explorer → Move with MoveSense. On Windows 11, open Show more options first.", "Нажмите правой кнопкой на файл или папку на рабочем столе или в Проводнике → «Перенести через MoveSense». В Windows 11 сначала откройте «Показать дополнительные параметры»."),
        ["ExplorerAction"] = ("Add Explorer menu", "Добавить пункт меню"),
        ["ExplorerRemove"] = ("Remove Explorer menu", "Убрать пункт меню"),
        ["LimitsHeading"] = ("Coverage in this preview", "Возможности этой версии"),
        ["LimitsText"] = ("Local Markdown and JSON paths can be repaired. Selected quoted configuration paths, project references and shortcut targets are review-only. Same-volume moves only; network shares and junctions are blocked. Text analysis: up to 4 MiB per file, 50,000 files per scan. Git, build and dependency folders are excluded from reference analysis. Unselected folders and unsupported formats are outside the analysis.", "Исправляются локальные пути Markdown и JSON. Пути в конфигурациях, проектах и ярлыках — только анализ. Перенос в пределах одного диска; сетевые пути и ссылки запрещены. Анализ текста: до 4 МиБ на файл, до 50 000 файлов. Каталоги Git, сборки и зависимостей исключены из анализа ссылок. Невыбранные папки и неподдерживаемые форматы не проверяются."),
        ["Ready"] = ("Ready · Analysis is read-only", "Готово · Анализ только для чтения"),
        ["Error"] = ("Could not complete the action", "Не удалось выполнить действие"),
        ["Analyzing"] = ("Analyzing selected folders…", "Анализ выбранных папок…"),
        ["Cancelled"] = ("Analysis cancelled. No files were changed.", "Анализ отменён. Файлы не изменены."),
        ["Changed"] = ("Paths changed. Analyze again before moving.", "Пути изменены. Повторите анализ."),
        ["NoNotes"] = ("No scan warnings. Coverage is limited to selected folders and supported formats.", "Замечаний нет. Проверены только выбранные папки и поддерживаемые форматы."),
        ["MoveConfirm"] = ("Move the selected item?", "Перенести выбранный объект?"),
        ["UndoConfirm"] = ("Restore the original location and reference files? Newer edits will block recovery.", "Вернуть исходное расположение и ссылки? При наличии новых правок откат будет запрещён."),
        ["SelectHistory"] = ("Select an operation first.", "Сначала выберите операцию."),
        ["Completed"] = ("Moved successfully", "Готово, перенесли"),
        ["Undone"] = ("Original location and references restored.", "Исходное расположение и ссылки восстановлены."),
        ["Moving"] = ("Moving and saving references… Please keep MoveSense open.", "Перенос и сохранение ссылок… Не закрывайте MoveSense."),
        ["Recovering"] = ("Checking files and recovering…", "Проверка и восстановление…"),
        ["Manual"] = ("Review", "Вручную"),
        ["Repairable"] = ("Repair", "Исправление"),
        ["Unchanged"] = ("Unchanged", "Без изменений"),
        ["ScopeRequired"] = ("Choose a file or folder and where to put it.", "Выберите файл или папку и место назначения."),
        ["NotesRequired"] = ("Review and acknowledge the unresolved references and scan notes first.", "Проверьте замечания и подтвердите, что неисправленные ссылки учтены."),
        ["ExplorerAdded"] = ("Explorer menu added for this account.", "Пункт меню добавлен для текущего пользователя."),
        ["ExplorerRemoved"] = ("Explorer menu removed.", "Пункт меню удалён."),
        ["MoveDetail"] = ("Repairs", "Исправлений"),
        ["Before"] = ("Before: ", "Было: "),
        ["After"] = ("After:  ", "Будет: "),
        ["NoRepair"] = ("No automatic repair. Update this reference manually after moving.", "Автоматического исправления нет. Обновите ссылку вручную после переноса."),
        ["SourceStep"] = ("1 · WHAT TO MOVE", "1 · ЧТО ПЕРЕНОСИМ"),
        ["DestinationStep"] = ("2 · WHERE TO", "2 · КУДА"),
        ["DropTitle"] = ("Drop a file or folder here", "Перетащите файл или папку сюда"),
        ["ChooseFolder"] = ("Choose folder…", "Выбрать папку…"),
        ["Desktop"] = ("Desktop", "Рабочий стол"),
        ["Documents"] = ("Documents", "Документы"),
        ["DestinationHelp"] = ("The current name is kept. You only need to choose a folder.", "Имя останется прежним. Нужно выбрать только папку."),
        ["Nearby"] = ("Also check links in the containing folder", "Проверить ссылки и в папке, где лежит файл"),
        ["NearbyHelp"] = ("Includes subfolders. Other locations are not checked.", "Включая вложенные папки. Остальные места не проверяются."),
        ["OnlySource"] = ("Only the selected item and any extra folder are checked.", "Проверяется только выбранный объект и дополнительная папка, если она указана."),
        ["MoreOptions"] = ("More options", "Дополнительно"),
        ["Rename"] = ("Name after moving", "Имя после переноса"),
        ["Details"] = ("Links and check details", "Ссылки и подробности проверки"),
        ["NoReferences"] = ("No supported references found in the checked locations.", "В проверенных местах поддерживаемые ссылки не найдены."),
        ["ReviewReady"] = ("Ready to move", "Можно переносить"),
        ["ReviewBlocked"] = ("Move is not available yet", "Перенос пока недоступен"),
        ["ReviewNeeded"] = ("Some links need your attention", "Некоторые ссылки нужно проверить"),
        ["NoBrokenLinks"] = ("No links that would break were found in the checked locations.", "В проверенных местах не нашли ссылок, которые сломаются при переносе."),
        ["RepairSummary"] = ("Links to fix automatically: {0}. Links left for manual review: {1}.", "Исправим автоматически: {0}. Потребуют ручной проверки: {1}."),
        ["Coverage"] = ("Checked files: {0}. Unsupported formats and other locations are not covered.", "Проверено файлов: {0}. Другие места и неподдерживаемые форматы не проверялись."),
        ["OpenFolder"] = ("Open folder", "Открыть папку"),
        ["QuickUndo"] = ("Put it back", "Вернуть обратно"),
        ["NewMove"] = ("Move another item", "Перенести ещё"),
        ["DesktopSetup"] = ("Desktop access", "С рабочего стола"),
        ["DesktopHelp"] = ("Create a shortcut, then drop one file or folder onto it. MoveSense will open the folder picker. Keep the portable app in a permanent location before creating the shortcut.", "Создайте ярлык и перетаскивайте на него один файл или папку. MoveSense сразу предложит выбрать место назначения. Перед созданием ярлыка поместите приложение в постоянную папку."),
        ["CreateShortcut"] = ("Create desktop shortcut", "Создать ярлык на рабочем столе"),
        ["ShortcutCreated"] = ("Shortcut ready. Drop a file or folder onto it to start.", "Ярлык готов. Перетащите на него файл или папку, чтобы начать."),
        ["ExplorerMenu"] = ("Move with MoveSense", "Перенести через MoveSense"),
        ["OneItem"] = ("Choose one file or folder at a time. Nothing has been moved.", "Выберите один файл или папку. Ничего не перенесено."),
        ["MissingSource"] = ("This file or folder no longer exists.", "Этот файл или папка больше не существует."),
        ["InvalidName"] = ("Enter a file or folder name, without a path.", "Введите имя файла или папки без пути."),
        ["SystemTheme"] = ("System", "Как в системе"),
        ["LightTheme"] = ("Light", "Светлая"),
        ["DarkTheme"] = ("Dark", "Тёмная"),
        ["StatusColumn"] = ("Status", "Статус"),
        ["TypeColumn"] = ("Type", "Тип"),
        ["OwnerColumn"] = ("Referenced from", "Где ссылка"),
        ["PathColumn"] = ("Current path", "Текущий путь"),
        ["DateColumn"] = ("Date", "Дата"),
        ["SourceColumn"] = ("Source", "Откуда"),
        ["DestinationColumn"] = ("Destination", "Куда"),
        ["RightClickHeading"] = ("Move from the right-click menu", "Перенос через правую кнопку мыши"),
        ["EnableMenu"] = ("Enable right-click menu", "Включить ПКМ"),
        ["RepairMenu"] = ("Update menu", "Обновить пункт ПКМ"),
        ["MenuEnabledShort"] = ("Enabled", "ПКМ включено"),
        ["MenuDisabled"] = ("Right-click a file or folder → Move with MoveSense. Enable it once here.", "ПКМ по файлу или папке → «Перенести через MoveSense». Включается один раз."),
        ["MenuEnabled"] = ("Ready: right-click a file or folder → Move with MoveSense. On Windows 11, use Show more options.", "Готово: ПКМ по файлу или папке → «Перенести через MoveSense». В Windows 11 — «Показать дополнительные параметры»."),
        ["MenuNeedsRepair"] = ("The menu points to another copy of MoveSense or is incomplete. Update it to use this copy.", "Пункт меню связан с другой копией MoveSense или настроен не полностью. Обновите его для этой версии."),
        ["MenuUnavailable"] = ("Could not check the right-click menu settings.", "Не удалось проверить настройки меню ПКМ."),
        ["ExplorerFailed"] = ("Could not verify the right-click menu settings. Try enabling it from MoveSense.", "Не удалось подтвердить настройку меню ПКМ. Попробуйте включить его из MoveSense."),
    };
    public static string Explain(string message)
    {
        if (Language != "ru") return message;
        return message switch
        {
            "Source does not exist." => "Исходный файл или папка больше не существует.",
            "Source and destination are the same." => "Этот объект уже находится в выбранной папке. Выберите другую папку.",
            "Destination already exists. Overwriting is never allowed." => "В выбранной папке уже есть объект с таким именем. Выберите другую папку или измените имя в разделе «Дополнительно».",
            "Destination is inside the source." => "Нельзя перенести папку внутрь самой себя. Выберите другое место.",
            "Destination parent must already exist." => "Папка назначения больше не существует. Выберите другую.",
            "Cross-volume moves are not supported in this version." => "Пока можно переносить только в пределах одного диска. Выберите папку на том же диске.",
            "Moving a drive root is not supported." => "Выберите отдельный файл или папку, а не весь диск.",
            "Network shares are not supported for move operations." => "Сетевые папки пока не поддерживаются. Выберите папку на локальном диске.",
            _ => message
        };
    }
    public static string T(string key) => Texts.TryGetValue(key, out var text) ? (Language == "ru" ? text.Ru : text.En) : key;
    public static void SetLanguage(string language)
    {
        Language = language;
        foreach (var key in Texts.Keys) Application.Current.Resources[key] = T(key);
    }
    public static void SetTheme(string theme)
    {
        var dark = theme == "Dark";
        if (theme == "System")
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        var colors = dark
            ? new[] { "#1C2228", "#242C33", "#EDF1F4", "#AAB7C2", "#3B464F", "#237E70", "#29443F" }
            : new[] { "#F4F5F7", "#FFFFFF", "#17212B", "#616D79", "#DFE3E8", "#166F65", "#E8F3F0" };
        var keys = new[] { "Background", "Surface", "Text", "Muted", "Line", "Accent", "AccentSoft" };
        for (var i = 0; i < keys.Length; i++)
            Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
}

