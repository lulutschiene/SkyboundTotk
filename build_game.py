"""Build SkyboundTOTK from an already-extracted TOTK 1.0.0 (or 1.2.1) dump.
Usage: python build_game.py [--version 1.0.0|1.2.1]. Each version has its own game, build and package folders, so
both can be installed side by side for the launcher (launcher/)."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import logging

ROOT = Path(__file__).resolve().parent
KIT = ROOT / "a64-kit"
WGPU = ROOT / "third_party" / "wgpu-native"
ICON = ROOT / "branding" / "skybound.ico"
PACKAGE = ROOT / "dist" / "Skybound"
# The original module IDs per game version (a prefix is enough); this is not a full content-integrity check.
VERSIONS = {
    "1.0.0": {"main": "082ce09b06e33a123cb1e2770f5f9147709033db", "sdk": "b9046c31eb5d31271be970fe732d38df49c6aa21"},
    "1.2.1": {"main": "9b4e4365", "sdk": "b9046c31eb5d31271be970fe732d38df49c6aa21"},
}
VERSION = "1.0.0"
GAME = ROOT / "game"
BUILD = ROOT / "build"
DIST = PACKAGE / "versions" / VERSION
EXPECTED_IDS = VERSIONS[VERSION]


def select_version(version):
    """1.0.0 keeps the original game/ and build/ folders; another version uses game-<v>/ and build-<v>/."""
    global VERSION, GAME, BUILD, DIST, EXPECTED_IDS
    VERSION = version
    suffix = "" if version == "1.0.0" else "-" + version
    GAME, BUILD = ROOT / ("game" + suffix), ROOT / ("build" + suffix)
    DIST = PACKAGE / "versions" / version
    EXPECTED_IDS = VERSIONS[version]


def setup_logging():
    log_file = ROOT / "build.log"
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s [%(levelname)s] %(message)s",
        handlers=[
            logging.FileHandler(log_file, encoding="utf-8")
        ]
    )
    logging.info("=== build started ===")


def log_and_print(message):
    print(message, flush=True)
    logging.info(message)


def fail(message):
    logging.error(message)
    raise SystemExit("ERROR: " + message)


def check_game():
    exefs, romfs = GAME / "exefs", GAME / "romfs"
    for name, expected in EXPECTED_IDS.items():
        path = exefs / name
        if not path.is_file():
            fail(f"Missing {path}. Put your extracted TOTK 1.0.0 ExeFS and RomFS in game/. See README.md.")
        with path.open("rb") as f:
            header = f.read(0x60)
        if len(header) < 0x60 or header[:4] != b"NSO0" or not header[0x40:0x60].rstrip(b"\0").hex().startswith(expected):
            fail(f"{path} is not the expected TOTK {VERSION} module. Only {', '.join(VERSIONS)} are supported.")
    if not (romfs / "Event").is_dir() or not (romfs / "ActorSystem").is_dir():
        fail(f"{romfs} does not look like an extracted TOTK RomFS. See README.md.")
    return exefs, romfs


def check_tools():
    if sys.version_info < (3, 10):
        fail("Python 3.10 or newer is required.")
    try:
        import lz4.block
    except ImportError:
        fail(f"Python package lz4 is missing. Install it with: \"{sys.executable}\" -m pip install lz4")
    sys.path.insert(0, str(KIT / "tools"))
    import toolchain
    for tool in ("clang", "clang++", "lld"):
        toolchain.need(toolchain.find(tool))
    if os.name == "nt":
        needed = (WGPU / "include/webgpu/webgpu.h", WGPU / "lib/wgpu_native.dll", WGPU / "lib/wgpu_native.dll.lib",
                  ROOT / "third_party/sdl2/SDL2.dll")
    else:
        needed = (WGPU / "include/webgpu/webgpu.h", WGPU / "lib/libwgpu_native.so")
        if subprocess.run(["pkg-config", "--exists", "sdl3", "libavformat", "libavcodec", "libavutil", "libswscale"]).returncode:
            fail("sdl3 and ffmpeg (libavformat, libavcodec, libavutil, libswscale) are required.")
    for path in needed:
        if not path.is_file():
            fail(f"Missing bundled runtime file: {path}")
    run(sys.executable, KIT / "lift/build_emit.py")


def run(*args):
    cmd_str = " ".join(str(x) for x in args)
    log_and_print(f"\n== {cmd_str}")

    process = subprocess.Popen(
        [str(x) for x in args],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        cwd=ROOT,
        bufsize=1,
        universal_newlines=True
    )

    for line in process.stdout:
        print(line, end="", flush=True)
        logging.info(line.rstrip("\n"))

    process.wait()
    if process.returncode != 0:
        logging.error(f"Command failed with exit code {process.returncode}")
        raise subprocess.CalledProcessError(process.returncode, process.args)


def lift_identity(exefs):
    digest = hashlib.sha256()
    for root in (KIT / "lift", KIT / "hos", KIT / "rt"):
        for path in sorted(root.rglob("*")):
            if path.is_file() and path.suffix in (".py", ".c", ".cpp", ".h"):
                digest.update(path.relative_to(ROOT).as_posix().encode())
                digest.update(path.read_bytes())
    for path in (KIT / "build.py", KIT / "tools/nso.py", ROOT / "ports/zelda-totk/hot_funcs.txt"):
        if path.is_file():
            digest.update(path.relative_to(ROOT).as_posix().encode())
            digest.update(path.read_bytes())
    for path in sorted(exefs.iterdir()):
        if path.is_file() and path.name in {"rtld", "main", "sdk"} | {f"subsdk{i}" for i in range(1, 10)}:
            digest.update(path.name.encode())
            with path.open("rb") as f:
                for chunk in iter(lambda: f.read(1024 * 1024), b""):
                    digest.update(chunk)
    return digest.hexdigest()


def package(romfs):
    native = BUILD / "totk/native"
    image = BUILD / "totk/image"
    DIST.mkdir(parents=True, exist_ok=True)
    # Refuse to silently replace a previous installation, including saves or mods.
    if any(DIST.iterdir()):
        fail(f"Standalone output {DIST} is not empty. Move it aside before rebuilding; keep its user folder.")
    if os.name == "nt":
        outputs = ((native / "totk.exe", "SkyboundTOTK.exe"),
                   (native / "wgpu_native.dll", "wgpu_native.dll"),
                   (ROOT / "third_party/sdl2/SDL2.dll", "SDL2.dll"),
                   (image / "image.bin", "image.bin"),
                   (image / "image.json", "image.json"))
    else:
        outputs = ((native / "totk", "SkyboundTOTK"),
                   (native / "libwgpu_native.so", "libwgpu_native.so"),
                   (image / "image.bin", "image.bin"),
                   (image / "image.json", "image.json"))
    for src, name in outputs:
        if not src.is_file():
            fail(f"Required build output is missing: {src}")
        shutil.copy2(src, DIST / name)
    if os.name == "nt" and ICON.is_file():
        add_icon(DIST / "SkyboundTOTK.exe", ICON)
    launcher = ROOT / "launcher" / "Skybound.exe"
    if launcher.is_file():
        try:
            shutil.copy2(launcher, PACKAGE / launcher.name)
        except PermissionError:
            log_and_print("The launcher is open, so its copy in the package was left as it is.")
    log_and_print(f"Copying RomFS to {DIST / 'romfs'}; this may take several minutes.")
    shutil.copytree(romfs, DIST / "romfs", symlinks=False)
    launch = DIST / ("SkyboundTOTK.exe" if os.name == "nt" else "SkyboundTOTK")
    log_and_print(f"\nBuild complete. Open {launch} to play.")


def add_icon(exe, ico):
    """Stores an .ico file's images in the executable as icon group 1 (Explorer, and the window: display.c)."""
    import ctypes
    from ctypes import wintypes
    data = ico.read_bytes()
    count = struct.unpack_from("<HHH", data)[2]
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.BeginUpdateResourceW.restype = wintypes.HANDLE
    k32.BeginUpdateResourceW.argtypes = (wintypes.LPCWSTR, wintypes.BOOL)
    k32.UpdateResourceW.argtypes = (wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, wintypes.WORD, ctypes.c_char_p,
                                    wintypes.DWORD)
    k32.EndUpdateResourceW.argtypes = (wintypes.HANDLE, wintypes.BOOL)
    handle = k32.BeginUpdateResourceW(str(exe), False)
    if not handle:
        fail(f"Could not open {exe} to add its icon (error {ctypes.get_last_error()}).")
    group = struct.pack("<HHH", 0, 1, count)
    for i in range(count):
        w, h, colours, _, planes, bpp, size, offset = struct.unpack_from("<BBBBHHII", data, 6 + 16 * i)
        k32.UpdateResourceW(handle, 3, i + 1, 0, data[offset:offset + size], size)   # RT_ICON
        group += struct.pack("<BBBBHHIH", w, h, colours, 0, planes, bpp, size, i + 1)
    k32.UpdateResourceW(handle, 14, 1, 0, group, len(group))   # RT_GROUP_ICON
    if not k32.EndUpdateResourceW(handle, False):
        fail(f"Could not add the icon to {exe} (error {ctypes.get_last_error()}).")
    log_and_print(f"Added the icon to {exe.name}.")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--version", choices=sorted(VERSIONS), default="1.0.0", help="game version to build")
    select_version(parser.parse_args().version)
    setup_logging()
    exefs, romfs = check_game()
    check_tools()
    # A matching completed lift is reusable; changed inputs or an interrupted lift need a fresh directory.
    identity = lift_identity(exefs)
    stamp = BUILD / "totk/lift-input.sha256"
    image = BUILD / "totk/image/image.json"
    generated = BUILD / "totk/gen"
    complete = image.is_file() and (generated / "a64_suyu.h").is_file() and any(generated.glob("code_*.c"))

    if complete and stamp.is_file() and stamp.read_text(encoding="ascii").strip() == identity:
        with image.open(encoding="utf-8") as f:
            modules = {m["name"]: m["build_id"] for m in json.load(f)["modules"]}
        if any(not (modules.get(name) or "").startswith(build_id) for name, build_id in EXPECTED_IDS.items()):
            fail("Existing build contains a different game version. Move the build folder aside before retrying.")
        log_and_print("Reusing the existing lifted base-game code.")
    else:
        if BUILD.exists() and any(BUILD.iterdir()):
            fail("Build inputs changed or the lift is incomplete. Move the build folder aside before rebuilding.")
        run(sys.executable, KIT / "build.py", "lift", "totk", "--exefs", exefs,
            "--skip", "subsdk0", "--build", BUILD)
        stamp.write_text(identity + "\n", encoding="ascii")

    run(sys.executable, KIT / "build.py", "native", "totk", "--build", BUILD, "--wgpu", WGPU)
    package(romfs)


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as error:
        logging.error(f"ERROR: command failed with exit code {error.returncode}")
        print(f"ERROR: command failed with exit code {error.returncode}", file=sys.stderr)
        sys.exit(error.returncode or 1)
