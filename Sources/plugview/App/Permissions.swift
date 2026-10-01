import AVFoundation

/// Camera and microphone access.
///
/// From a terminal, macOS grants it to the terminal app.
enum Permissions {
    static func isGranted(for mediaType: AVMediaType) async -> Bool {
        switch AVCaptureDevice.authorizationStatus(for: mediaType) {
        case .authorized: true
        case .notDetermined: await AVCaptureDevice.requestAccess(for: mediaType)
        case .denied, .restricted: false
        @unknown default: false
        }
    }

    static let cameraDeniedMessage = """
        camera access is denied, so the capture card cannot be shown.
        Allow your terminal app in System Settings › Privacy & Security › Camera, then run plugview again.
        """

    static let microphoneDeniedMessage =
        "microphone access is denied, continuing without sound. Allow it in System Settings › Privacy & Security › Microphone."
}
