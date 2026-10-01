import AVFoundation
import PlugviewCore

extension AVCaptureDevice {
    /// Selects the best mode for live play (see `bestModeIndex`) at its fastest frame rate.
    ///
    /// Leaves the device locked on success: `AVCaptureSession.startRunning()` resets the frame rate to the
    /// device default (20 fps on MS2130 cards) unless the device stays locked until the session runs.
    /// The caller must call `unlockForConfiguration()` once the session is running.
    func lockBestFormat() throws(CaptureError) -> VideoMode {
        let modes = formats.map(VideoMode.init(format:))
        guard let index = bestModeIndex(in: modes) else { throw .noVideoFormats(name: localizedName) }

        do {
            try lockForConfiguration()
        } catch {
            throw .cannotOpen(name: localizedName, cause: error)
        }

        let format = formats[index]
        activeFormat = format
        if let fastestRange = format.videoSupportedFrameRateRanges.max(by: { $0.maxFrameRate < $1.maxFrameRate }) {
            activeVideoMinFrameDuration = fastestRange.minFrameDuration
            activeVideoMaxFrameDuration = fastestRange.minFrameDuration
        }
        return modes[index]
    }

    /// Frame rate the running device actually delivers, which can differ from the one requested.
    var activeFrameRate: Double { 1 / activeVideoMinFrameDuration.seconds }
}

private let compressedCodecTypes: Set<CMVideoCodecType> = [
    kCMVideoCodecType_JPEG, kCMVideoCodecType_JPEG_OpenDML, kCMVideoCodecType_H264, kCMVideoCodecType_HEVC,
]

private let nativePixelFormats: Set<OSType> = [kCVPixelFormatType_422YpCbCr8_yuvs, kCVPixelFormatType_422YpCbCr8]

extension VideoMode {
    init(format: AVCaptureDevice.Format) {
        let dimensions = CMVideoFormatDescriptionGetDimensions(format.formatDescription)
        let subtype = CMFormatDescriptionGetMediaSubType(format.formatDescription)
        let encoding: FrameEncoding =
            if compressedCodecTypes.contains(subtype) {
                .compressed
            } else if nativePixelFormats.contains(subtype) {
                .native
            } else {
                .converted
            }
        self.init(
            width: Int(dimensions.width),
            height: Int(dimensions.height),
            maxFrameRate: format.videoSupportedFrameRateRanges.map(\.maxFrameRate).max() ?? 0,
            encoding: encoding
        )
    }
}
