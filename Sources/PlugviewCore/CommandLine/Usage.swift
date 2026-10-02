/// Text printed by `plugview --help`.
public let usageText = """
    plugview — watch and play a capture device (e.g. Nintendo Switch) in a window.

    USAGE
      plugview [options]

    OPTIONS
      -d, --device <name>   Video device to use (name substring or unique ID).
                            Default: first USB capture device found.
      -a, --audio <name>    Audio input to play (name substring or unique ID).
                            Default: the audio input that belongs to the video device.
          --no-audio        Do not play audio.
      -v, --volume <0-100>  Start volume in percent. Default: 100.
      -f, --fullscreen      Start in full screen.
          --mode <WxH@fps>  Capture mode, e.g. 1280x720@60 (see --list).
                            Default: the smoothest mode the device offers.
      -l, --list            List video and audio devices, then exit.
          --measure         Measure the capture pipeline's latency for a few seconds, then exit.
      -h, --help            Show this help.
          --version         Show the version.

    KEYS (in the window)
      F            Toggle full screen        Esc   Leave full screen
      M            Mute / unmute             ↑ ↓   Volume up / down
      S            Save a screenshot         ⌘0    Actual size
      Q, ⌘Q        Quit                      ⌘M    Minimize

    Plug and play: start plugview before or after connecting the device;
    it attaches automatically and re-attaches if the cable is unplugged.
    """
