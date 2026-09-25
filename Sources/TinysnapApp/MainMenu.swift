import AppKit

/// Never shown, since Tinysnap has no Dock icon, but its key equivalents are what make
/// ⌘C, ⌘Z, ⌘S and the rest work in the editor and inside a text being typed. Every
/// item targets the first responder, so a text field gets its own copy and paste.
@MainActor
enum MainMenu {
    /// Sets the main menu, and hands AppKit the Window menu, which then lists every open
    /// capture by name.
    static func install() {
        let menu = build()
        NSApp.mainMenu = menu
        NSApp.windowsMenu = menu.item(withTitle: "Window")?.submenu
    }

    private static func build() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(submenu("Tinysnap", [
            item("Settings...", "showSettings:", ","),
            .separator(),
            item("Quit Tinysnap", "terminate:", "q"),
        ]))
        menu.addItem(submenu("File", [
            item("Save", "saveImage:", "s"),
            item("Save As...", "saveImageAs:", "S"),
            item("Close", "performClose:", "w"),
        ]))
        menu.addItem(submenu("Edit", [
            item("Undo", "undo:", "z"),
            item("Redo", "redo:", "Z"),
            .separator(),
            item("Cut", "cut:", "x"),
            item("Copy", "copy:", "c"),
            item("Paste", "paste:", "v"),
            item("Delete", "delete:", ""),
            item("Select All", "selectAll:", "a"),
            .separator(),
            item("Copy Text", "copyText:", "C"),
            item("Pin", "pinImage:", "p"),
        ]))
        menu.addItem(submenu("View", [
            item("Zoom In", "zoomIn:", "="),
            item("Zoom Out", "zoomOut:", "-"),
            item("Zoom to Fit", "zoomToFit:", "0"),
            item("Actual Size", "zoomToActualSize:", "1"),
        ]))
        menu.addItem(submenu("Window", [
            item("Minimize", "performMiniaturize:", "m"),
            item("Zoom", "performZoom:", ""),
            .separator(),
            item("Bring All to Front", "arrangeInFront:", ""),
        ]))
        return menu
    }

    /// An upper case key equivalent adds Shift, which is how ⌘⇧S and ⌘⇧Z are written.
    private static func item(_ title: String, _ action: String, _ key: String) -> NSMenuItem {
        NSMenuItem(title: title, action: NSSelectorFromString(action), keyEquivalent: key)
    }

    private static func submenu(_ title: String, _ items: [NSMenuItem]) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        let menu = NSMenu(title: title)
        items.forEach { menu.addItem($0) }
        item.submenu = menu
        return item
    }
}
