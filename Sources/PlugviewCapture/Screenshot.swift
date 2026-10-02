import CoreImage
import Foundation
import ImageIO
import PlugviewCore
import UniformTypeIdentifiers

/// Writes a captured frame as a PNG in `~/Pictures/plugview`.
enum Screenshot {
    enum SaveError: Error, CustomStringConvertible {
        case cannotRender
        case cannotWrite(URL)

        var description: String {
            switch self {
            case .cannotRender: "the frame could not be converted to an image"
            case .cannotWrite(let url): "could not write \(url.path)"
            }
        }
    }

    static var directory: URL { URL.picturesDirectory.appending(path: "plugview", directoryHint: .isDirectory) }

    /// Saves the frame as it looks on screen, with the same color fix when `expandsLimitedRange` is on.
    static func save(_ pixelBuffer: CVPixelBuffer, expandsLimitedRange: Bool, at date: Date = .now) throws -> URL {
        var image = CIImage(cvPixelBuffer: pixelBuffer)
        if expandsLimitedRange { image = image.applyingFilter("CIColorMatrix", parameters: colorMatrixParameters) }
        guard let colorSpace = CGColorSpace(name: CGColorSpace.sRGB),
            let cgImage = context.createCGImage(image, from: image.extent, format: .RGBA8, colorSpace: colorSpace)
        else { throw SaveError.cannotRender }

        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let parts = Calendar.current.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
        let url = directory.appending(
            path: screenshotFileName(
                year: parts.year ?? 0, month: parts.month ?? 0, day: parts.day ?? 0, hour: parts.hour ?? 0, minute: parts.minute ?? 0,
                second: parts.second ?? 0))
        guard let destination = CGImageDestinationCreateWithURL(url as CFURL, UTType.png.identifier as CFString, 1, nil) else {
            throw SaveError.cannotWrite(url)
        }
        CGImageDestinationAddImage(destination, cgImage, nil)
        guard CGImageDestinationFinalize(destination) else { throw SaveError.cannotWrite(url) }
        return url
    }

    /// `CIColorMatrix` inputs for `LimitedRangeExpansion`, the same values the preview layer's filter uses.
    static var colorMatrixParameters: [String: Any] {
        let scale = CGFloat(LimitedRangeExpansion.scale)
        let bias = CGFloat(LimitedRangeExpansion.bias)
        return [
            "inputRVector": CIVector(x: scale, y: 0, z: 0, w: 0),
            "inputGVector": CIVector(x: 0, y: scale, z: 0, w: 0),
            "inputBVector": CIVector(x: 0, y: 0, z: scale, w: 0),
            "inputBiasVector": CIVector(x: bias, y: bias, z: bias, w: 0),
        ]
    }

    private static let context = CIContext()
}
