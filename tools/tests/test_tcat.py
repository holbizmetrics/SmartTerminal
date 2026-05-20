"""pytest suite for tcat CLI (OSC 1338 emitter)."""
import base64
import subprocess
import sys
from pathlib import Path

import pytest

TOOLS = Path(__file__).resolve().parent.parent
TCAT = TOOLS / "tcat"
FIXTURES = Path(__file__).resolve().parent / "fixtures"


def run(args: list[str], stdin: bytes | None = None) -> subprocess.CompletedProcess:
    return subprocess.run(
        [sys.executable, str(TCAT), *args],
        input=stdin, capture_output=True, check=False,
    )


def parse_osc1338(output: bytes) -> dict[str, str]:
    """Split an OSC 1338 sequence into its parameter dict."""
    assert output.startswith(b"\x1b]1338;"), f"missing OSC 1338 prefix: {output[:16]!r}"
    assert output.endswith(b"\x07"), f"missing BEL terminator: {output[-8:]!r}"
    body = output[len(b"\x1b]1338;"):-1].decode("ascii")
    params: dict[str, str] = {}
    for part in body.split(";"):
        if "=" in part:
            k, v = part.split("=", 1)
            params[k] = v
    return params


def test_emit_mermaid_text(tmp_path: Path) -> None:
    src = b"graph TD\n  A-->B\n"
    f = tmp_path / "diagram.mmd"
    f.write_bytes(src)
    r = run([str(f)])
    assert r.returncode == 0, r.stderr.decode()
    params = parse_osc1338(r.stdout)
    assert params["type"] == "mermaid"
    assert base64.standard_b64decode(params["data"]) == src
    # mermaid is a text type — no MIME field
    assert "mime" not in params


def test_emit_svg(tmp_path: Path) -> None:
    src = b'<svg xmlns="http://www.w3.org/2000/svg"><circle r="5"/></svg>'
    f = tmp_path / "shape.svg"
    f.write_bytes(src)
    r = run([str(f)])
    assert r.returncode == 0, r.stderr.decode()
    params = parse_osc1338(r.stdout)
    assert params["type"] == "svg"
    assert base64.standard_b64decode(params["data"]) == src
    assert "mime" not in params


def test_emit_image_includes_mime(tmp_path: Path) -> None:
    # Minimal valid 1x1 PNG
    png = bytes.fromhex(
        "89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c4"
        "890000000a49444154789c6300010000000500010d0a2db40000000049454e44"
        "ae426082"
    )
    f = tmp_path / "tiny.png"
    f.write_bytes(png)
    r = run([str(f)])
    assert r.returncode == 0, r.stderr.decode()
    params = parse_osc1338(r.stdout)
    assert params["type"] == "image"
    assert params["mime"] == "image/png"
    assert base64.standard_b64decode(params["data"]) == png


def test_emit_audio_default_mime(tmp_path: Path) -> None:
    f = tmp_path / "clip.mp3"
    f.write_bytes(b"\xff\xfb\x90\x00" + b"\x00" * 100)  # fake mp3 header + padding
    r = run([str(f)])
    assert r.returncode == 0, r.stderr.decode()
    params = parse_osc1338(r.stdout)
    assert params["type"] == "audio"
    assert params["mime"] == "audio/mpeg"


@pytest.mark.parametrize("ext,expected_type", [
    (".mmd", "mermaid"), (".mermaid", "mermaid"),
    (".svg", "svg"),
    (".png", "image"), (".jpg", "image"), (".jpeg", "image"),
    (".gif", "image"), (".webp", "image"),
    (".mp3", "audio"), (".ogg", "audio"), (".wav", "audio"),
])
def test_detect_type_from_extension(tmp_path: Path, ext: str, expected_type: str) -> None:
    f = tmp_path / f"file{ext}"
    f.write_bytes(b"x" * 16)
    r = run([str(f)])
    assert r.returncode == 0, r.stderr.decode()
    params = parse_osc1338(r.stdout)
    assert params["type"] == expected_type


def test_size_limit_rejection(tmp_path: Path) -> None:
    # mermaid limit is 65536 bytes; emit 70000 bytes of source
    f = tmp_path / "huge.mmd"
    f.write_bytes(b"a" * 70000)
    r = run([str(f)])
    assert r.returncode != 0
    assert b"exceeds" in r.stderr


def test_stdin_requires_type_flag() -> None:
    r = run([], stdin=b"graph TD; A-->B")
    assert r.returncode == 2
    assert b"--type required" in r.stderr


def test_unknown_extension_without_type_flag_errors(tmp_path: Path) -> None:
    f = tmp_path / "what.xyz"
    f.write_bytes(b"unknown")
    r = run([str(f)])
    assert r.returncode == 2
    assert b"unknown extension" in r.stderr


def test_explicit_type_override_for_unknown_extension(tmp_path: Path) -> None:
    f = tmp_path / "diagram.txt"
    f.write_bytes(b"graph TD; A-->B")
    r = run(["--type", "mermaid", str(f)])
    assert r.returncode == 0, r.stderr.decode()
    params = parse_osc1338(r.stdout)
    assert params["type"] == "mermaid"


def test_stdin_with_type_works() -> None:
    r = run(["--type", "mermaid"], stdin=b"graph TD\n  X-->Y\n")
    assert r.returncode == 0, r.stderr.decode()
    params = parse_osc1338(r.stdout)
    assert params["type"] == "mermaid"
    assert base64.standard_b64decode(params["data"]) == b"graph TD\n  X-->Y\n"


def test_no_file_no_such_path(tmp_path: Path) -> None:
    r = run([str(tmp_path / "missing.mmd")])
    assert r.returncode == 1
    assert b"no such file" in r.stderr


def test_missing_file_returns_one(tmp_path: Path) -> None:
    r = run([str(tmp_path / "absent.svg")])
    assert r.returncode == 1
