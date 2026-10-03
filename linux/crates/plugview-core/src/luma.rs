/// Where brightness sits in a frame's bytes, for `max_luma`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct LumaLayout {
    /// Offset of the first brightness byte in a row.
    pub first_byte: usize,
    /// Bytes from one pixel's brightness to the next pixel's.
    pub bytes_per_pixel: usize,
    /// Brightness bytes per pixel: 1 for Y, 3 for R, G and B (the brightest one counts).
    pub channels: usize,
}

impl LumaLayout {
    /// Packed 4:2:2 with Y first (YUY2, YVYU): Y0 U Y1 V.
    pub const Y_FIRST_422: Self = Self { first_byte: 0, bytes_per_pixel: 2, channels: 1 };
    /// Packed 4:2:2 with chroma first (UYVY): U Y0 V Y1.
    pub const CHROMA_FIRST_422: Self = Self { first_byte: 1, bytes_per_pixel: 2, channels: 1 };
    /// The Y plane of a planar or biplanar frame (NV12, I420).
    pub const LUMA_PLANE: Self = Self { first_byte: 0, bytes_per_pixel: 1, channels: 1 };
    /// 32-bit RGB with color first (`BGRx`, `RGBA`).
    pub const RGB_FIRST_32: Self = Self { first_byte: 0, bytes_per_pixel: 4, channels: 3 };
    /// 32-bit RGB with the padding byte first (`xRGB`, `ABGR`).
    pub const PADDING_FIRST_32: Self = Self { first_byte: 1, bytes_per_pixel: 4, channels: 3 };
}

/// The brightest value (0–255) on a coarse grid of a frame, enough to tell a black frame from a picture.
///
/// Reads every `step`-th pixel of every `step`-th row, so a 1080p frame at step 8 costs about 32 000 byte reads.
#[must_use]
pub fn max_luma(bytes: &[u8], width: usize, height: usize, bytes_per_row: usize, layout: LumaLayout, step: usize) -> u8 {
    if width == 0 || height == 0 || step == 0 {
        return 0;
    }
    let mut brightest = 0;
    for row in (0..height).step_by(step) {
        for column in (0..width).step_by(step) {
            let pixel = row * bytes_per_row + layout.first_byte + column * layout.bytes_per_pixel;
            for value in bytes.iter().skip(pixel).take(layout.channels) {
                brightest = brightest.max(*value);
            }
        }
    }
    brightest
}

#[cfg(test)]
mod tests {
    use super::*;

    const LAYOUTS: [LumaLayout; 5] =
        [LumaLayout::Y_FIRST_422, LumaLayout::CHROMA_FIRST_422, LumaLayout::LUMA_PLANE, LumaLayout::RGB_FIRST_32, LumaLayout::PADDING_FIRST_32];

    /// A 64×32 frame in `layout` filled with `fill`, with rows padded by 16 bytes.
    fn frame(layout: LumaLayout, fill: u8, bright: Option<(usize, usize, u8)>) -> (Vec<u8>, usize) {
        let bytes_per_row = 64 * layout.bytes_per_pixel + 16;
        let mut bytes = vec![fill; bytes_per_row * 32];
        if let Some((x, y, value)) = bright {
            bytes[y * bytes_per_row + layout.first_byte + x * layout.bytes_per_pixel] = value;
        }
        (bytes, bytes_per_row)
    }

    #[test]
    fn video_black_stays_black() {
        for layout in LAYOUTS {
            let (bytes, bytes_per_row) = frame(layout, 16, None);
            assert_eq!(max_luma(&bytes, 64, 32, bytes_per_row, layout, 8), 16, "{layout:?}");
        }
    }

    #[test]
    fn finds_a_bright_pixel_on_the_grid() {
        for layout in LAYOUTS {
            let (bytes, bytes_per_row) = frame(layout, 16, Some((8, 16, 200)));
            assert_eq!(max_luma(&bytes, 64, 32, bytes_per_row, layout, 8), 200, "{layout:?}");
        }
    }

    #[test]
    fn ignores_chroma_bytes() {
        let bytes: Vec<u8> = (0..64 * 2 * 4).map(|index| if index % 2 == 0 { 16 } else { 128 }).collect();
        assert_eq!(max_luma(&bytes, 64, 4, 128, LumaLayout::Y_FIRST_422, 8), 16);
    }

    #[test]
    fn empty_or_short_frame_reads_what_exists() {
        assert_eq!(max_luma(&[], 0, 0, 0, LumaLayout::LUMA_PLANE, 8), 0);
        assert_eq!(max_luma(&[40], 64, 32, 64, LumaLayout::LUMA_PLANE, 8), 40);
    }
}
