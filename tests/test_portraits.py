import os

import pytest

from conftest import SAMPLE_DIR, sample_name
from zc import portraits, savefile

needs_libs = pytest.mark.skipif(not portraits.libraries_available(), reason="OpenEXR/numpy/Pillow not installed")


@pytest.fixture(scope="module")
def blobs():
    return savefile.load_bytes(open(os.path.join(SAMPLE_DIR, sample_name()), "rb").read()).blobs


def test_index_finds_operator_portraits(blobs):
    p = portraits.Portraits(blobs)
    guids = p.guids()
    assert guids and all(len(g) == 32 and g == g.upper() for g in guids)


def test_exr_bytes_are_extracted_exactly(blobs):
    p = portraits.Portraits(blobs)
    exr = portraits.exr_bytes(p._entries[p.guids()[0]])
    assert exr and exr[:4] == b"\x76\x2f\x31\x01"                 # OpenEXR magic


def test_garbage_bin_returns_none():
    assert portraits.exr_bytes(b"") is None
    assert portraits.exr_bytes(b"\x00" * 64) is None
    assert portraits.exr_to_png(b"not an exr") is None


@needs_libs
def test_png_is_a_real_square_portrait(blobs):
    from PIL import Image
    import io
    p = portraits.Portraits(blobs)
    png = p.png(p.guids()[0])
    assert png and png[:8] == b"\x89PNG\r\n\x1a\n"
    img = Image.open(io.BytesIO(png))
    assert img.mode == "RGBA" and img.size[0] == img.size[1] >= 128
    assert img.getchannel("A").getextrema()[1] > 0                 # not fully transparent
    assert p.png(p.guids()[0]) is png                              # memoised
    assert p.png("0" * 32) is None
