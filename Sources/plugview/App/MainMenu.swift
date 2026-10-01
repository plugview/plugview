import AppKit

/// The app's menu bar: Quit, Toggle Full Screen, Minimize, and Zoom with their standard shortcuts.
@MainActor
func makeMainMenu() -> NSMenu {
    let appMenu = NSMenu()
    appMenu.addItem(withTitle: "Quit plugview", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")

    let viewMenu = NSMenu(title: "View")
    let fullScreenItem = viewMenu.addItem(withTitle: "Toggle Full Screen", action: #selector(NSWindow.toggleFullScreen(_:)), keyEquivalent: "f")
    fullScreenItem.keyEquivalentModifierMask = [.control, .command]

    let windowMenu = NSMenu(title: "Window")
    windowMenu.addItem(withTitle: "Minimize", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
    windowMenu.addItem(withTitle: "Zoom", action: #selector(NSWindow.performZoom(_:)), keyEquivalent: "")
    NSApp.windowsMenu = windowMenu

    let mainMenu = NSMenu()
    for submenu in [appMenu, viewMenu, windowMenu] {
        mainMenu.addItem(withTitle: "", action: nil, keyEquivalent: "").submenu = submenu
    }
    return mainMenu
}
