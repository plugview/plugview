import PlugviewCore
import Testing

@Suite struct LumaSamplingTests {
    /// A width×height frame in `layout` filled with `fill`, with `bright` at the brightness byte of pixel (x, y).
    private func frame(width: Int, height: Int, layout: LumaLayout, fill: UInt8, bright: (x: Int, y: Int, value: UInt8)? = nil) -> [UInt8] {
        let bytesPerRow = width * layout.bytesPerPixel + 16
        var bytes = [UInt8](repeating: fill, count: bytesPerRow * height)
        if let bright { bytes[bright.y * bytesPerRow + layout.firstByte + bright.x * layout.bytesPerPixel] = bright.value }
        return bytes
    }

    private func luma(_ bytes: [UInt8], width: Int, height: Int, layout: LumaLayout) -> Int {
        bytes.withUnsafeBytes { maxLuma(in: $0, width: width, height: height, bytesPerRow: width * layout.bytesPerPixel + 16, layout: layout) }
    }

    @Test(arguments: [LumaLayout.yFirst422, .chromaFirst422, .lumaPlane, .bgra])
    func videoBlackStaysBlack(layout: LumaLayout) {
        #expect(luma(frame(width: 64, height: 32, layout: layout, fill: 16), width: 64, height: 32, layout: layout) == 16)
    }

    @Test(arguments: [LumaLayout.yFirst422, .chromaFirst422, .lumaPlane, .bgra])
    func findsABrightPixelOnTheGrid(layout: LumaLayout) {
        let bytes = frame(width: 64, height: 32, layout: layout, fill: 16, bright: (x: 8, y: 16, value: 200))
        #expect(luma(bytes, width: 64, height: 32, layout: layout) == 200)
    }

    @Test func ignoresChromaBytes() {
        // Chroma at 128 everywhere, luma black: still black.
        var bytes = [UInt8](repeating: 128, count: 64 * 2 * 4)
        for index in stride(from: 0, to: bytes.count, by: 2) { bytes[index] = 16 }
        #expect(bytes.withUnsafeBytes { maxLuma(in: $0, width: 64, height: 4, bytesPerRow: 128, layout: .yFirst422) } == 16)
    }

    @Test func emptyFrameIsBlack() {
        #expect([UInt8]().withUnsafeBytes { maxLuma(in: $0, width: 0, height: 0, bytesPerRow: 0, layout: .lumaPlane) } == 0)
    }
}
