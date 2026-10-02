import AppKit

/// The app's menu bar: About, Settings and updates (app launch only), Hide, Quit, Toggle Full Screen, Minimize, Zoom.
@MainActor
func makeMainMenu(hasMenuBarItem: Bool) -> NSMenu {
    let appMenu = NSMenu()
    appMenu.addItem(withTitle: "About plugview", action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), keyEquivalent: "")
    if hasMenuBarItem {
        // AppDelegate handles both; the menu bar panel shows the settings and the update result.
        appMenu.addItem(withTitle: "Check for Updates…", action: #selector(AppDelegate.checkForUpdates(_:)), keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Settings…", action: #selector(AppDelegate.showSettings(_:)), keyEquivalent: ",")
    }
    appMenu.addItem(.separator())
    appMenu.addItem(withTitle: "Hide plugview", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
    appMenu.addItem(withTitle: "Quit plugview", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")

    let viewMenu = NSMenu(title: "View")
    let fullScreenItem = viewMenu.addItem(withTitle: "Toggle Full Screen", action: #selector(NSWindow.toggleFullScreen(_:)), keyEquivalent: "f")
    fullScreenItem.keyEquivalentModifierMask = [.control, .command]
    viewMenu.addItem(.separator())
    // PlayerWindowController handles these through the responder chain.
    viewMenu.addItem(withTitle: "Actual Size", action: #selector(PlayerWindowController.showActualSize(_:)), keyEquivalent: "0")
    viewMenu.addItem(withTitle: "Zoom In", action: #selector(PlayerWindowController.zoomVideoIn(_:)), keyEquivalent: "+")
    viewMenu.addItem(withTitle: "Zoom Out", action: #selector(PlayerWindowController.zoomVideoOut(_:)), keyEquivalent: "-")
    viewMenu.addItem(.separator())
    let keepOnTopItem = viewMenu.addItem(withTitle: "Keep on Top", action: #selector(PlayerWindowController.toggleKeepOnTop(_:)), keyEquivalent: "t")
    keepOnTopItem.keyEquivalentModifierMask = [.option, .command]

    let windowMenu = NSMenu(title: "Window")
    windowMenu.addItem(withTitle: "Minimize", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
    windowMenu.addItem(withTitle: "Zoom", action: #selector(NSWindow.performZoom(_:)), keyEquivalent: "")
    windowMenu.addItem(.separator())
    windowMenu.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
    NSApp.windowsMenu = windowMenu

    let mainMenu = NSMenu()
    for submenu in [appMenu, viewMenu, windowMenu] {
        mainMenu.addItem(withTitle: "", action: nil, keyEquivalent: "").submenu = submenu
    }
    return mainMenu
}
