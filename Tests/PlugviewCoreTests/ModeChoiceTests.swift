import PlugviewCore
import Testing

@Suite struct ModeChoiceTests {
    let fullHD60 = VideoMode(width: 1920, height: 1080, maxFrameRate: 60, encoding: .native)
    let fullHD60Converted = VideoMode(width: 1920, height: 1080, maxFrameRate: 60, encoding: .converted)
    let fullHD30MJPEG = VideoMode(width: 1920, height: 1080, maxFrameRate: 30, encoding: .compressed)
    let hd60 = VideoMode(width: 1280, height: 720, maxFrameRate: 59.94, encoding: .native)
    let uhd30 = VideoMode(width: 3840, height: 2160, maxFrameRate: 30, encoding: .compressed)

    @Test func roundTripsThroughItsKey() {
        let choice = ModeChoice(hd60)
        #expect(choice.key == "1280x720@60")
        #expect(ModeChoice(key: choice.key) == choice)
        #expect(choice.title == "1280×720 · 60 fps")
    }

    @Test(arguments: ["", "1920x1080", "1920x1080@", "x1080@60", "1920x1080@60@1", "0x1080@60", "1920x1080@-60", "wide"])
    func rejectsMalformedKeys(key: String) {
        #expect(ModeChoice(key: key) == nil)
    }

    @Test func offersEachSizeAndRateOnceLargestFirst() {
        let choices = pickableModes(in: [hd60, fullHD60Converted, fullHD30MJPEG, fullHD60, uhd30])
        #expect(choices.map(\.key) == ["3840x2160@30", "1920x1080@60", "1920x1080@30", "1280x720@60"])
    }

    @Test func startsTheChosenModeWithTheCheapestEncoding() {
        let modes = [fullHD60Converted, hd60, fullHD60]
        #expect(modeIndex(in: modes, choice: ModeChoice(fullHD60)) == 2)
        #expect(modeIndex(in: modes, choice: ModeChoice(hd60)) == 1)
    }

    @Test func fallsBackToTheBestModeWithoutAChoiceOrWhenItIsGone() {
        let modes = [uhd30, fullHD60, hd60]
        #expect(modeIndex(in: modes, choice: nil) == 1)
        #expect(modeIndex(in: modes, choice: ModeChoice(width: 2560, height: 1440, frameRate: 60)) == 1)
    }
}

@Suite struct ReleaseVersionTests {
    @Test(arguments: [("v0.3.0", "0.2.0"), ("0.2.1", "0.2.0"), ("1.0", "0.9.9"), ("v0.10.0", "0.9.0"), ("0.2.0.1", "0.2.0")])
    func findsNewerVersions(candidate: String, current: String) {
        #expect(isVersion(candidate, newerThan: current))
    }

    @Test(arguments: [("v0.2.0", "0.2.0"), ("0.2", "0.2.0"), ("0.1.9", "0.2.0"), ("v1.0.0-beta", "0.2.0"), ("", "0.2.0"), ("v", "0.2.0")])
    func ignoresSameOlderOrMalformedVersions(candidate: String, current: String) {
        #expect(!isVersion(candidate, newerThan: current))
    }
}
