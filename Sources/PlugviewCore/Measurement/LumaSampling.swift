/// Where brightness sits in a frame's bytes, for `maxLuma`.
public struct LumaLayout: Equatable, Sendable {
    /// Offset of the first brightness byte in a row.
    public let firstByte: Int
    /// Bytes from one pixel's brightness to the next pixel's.
    public let bytesPerPixel: Int
    /// Brightness bytes per pixel: 1 for Y, 3 for B, G and R (the brightest one counts).
    public let channels: Int

    /// Packed 4:2:2 with Y first (`yuvs`, YUY2): Y0 U Y1 V.
    public static let yFirst422 = LumaLayout(firstByte: 0, bytesPerPixel: 2, channels: 1)
    /// Packed 4:2:2 with chroma first (`2vuy`, UYVY): U Y0 V Y1.
    public static let chromaFirst422 = LumaLayout(firstByte: 1, bytesPerPixel: 2, channels: 1)
    /// The Y plane of a biplanar 4:2:0 frame (`420v`, `420f`, NV12).
    public static let lumaPlane = LumaLayout(firstByte: 0, bytesPerPixel: 1, channels: 1)
    /// 32-bit BGRA.
    public static let bgra = LumaLayout(firstByte: 0, bytesPerPixel: 4, channels: 3)
}

/// The brightest value (0–255) on a coarse grid of a frame, enough to tell a black frame from a picture.
///
/// Reads every `step`-th pixel of every `step`-th row, so a 1080p frame costs about 32 000 byte reads.
public func maxLuma(in bytes: UnsafeRawBufferPointer, width: Int, height: Int, bytesPerRow: Int, layout: LumaLayout, step: Int = 8) -> Int {
    guard width > 0, height > 0, step > 0 else { return 0 }
    var brightest: UInt8 = 0
    for row in stride(from: 0, to: height, by: step) {
        for column in stride(from: 0, to: width, by: step) {
            let pixel = row * bytesPerRow + layout.firstByte + column * layout.bytesPerPixel
            for channel in 0..<layout.channels where pixel + channel < bytes.count {
                brightest = max(brightest, bytes[pixel + channel])
            }
        }
    }
    return Int(brightest)
}
