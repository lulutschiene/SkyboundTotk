"""Skybound's "Update and rebuild" (launcher: Help, Check for updates): brings the source up to date with GitHub while
keeping Skybound's own changes, then rebuilds the launcher and every installed game version. Saves, mods and settings
move from the old package into the new one. When a build fails, the old build and package are put back.
Standard library only. Usage: python launcher/update.py [--no-pull]"""
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parent.parent
PACKAGE = ROOT / "dist" / "Skybound"
KEEP = ("user", "mods", "language.txt", "rxcache")   # what a version's package holds besides the build outputs


def say(message):
    print(message, flush=True)


def git(*args, check=True):
    result = subprocess.run(["git", "-C", str(ROOT), *args], capture_output=True, text=True)
    if check and result.returncode:
        raise SystemExit(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result


UPSTREAM = "origin/main"   # (the original project; Skybound's own changes are commits on top of it)


def update_source():
    old = git("rev-parse", "HEAD").stdout.strip()
    git("fetch", "--quiet", "origin")
    if git("rev-list", "--count", "HEAD.." + UPSTREAM).stdout.strip() == "0":
        say("The source is already up to date.")
        return
    dirty = git("status", "--porcelain", "--untracked-files=no").stdout.strip() != ""
    if dirty:
        git("stash", "push", "--quiet", "-m", "skybound update")
    if git("merge", "--no-edit", "--quiet", UPSTREAM, check=False).returncode:
        git("merge", "--abort", check=False)
        if dirty:
            git("stash", "pop", "--quiet")
        raise SystemExit("The new GitHub changes touch the same lines as Skybound's own changes (input modes, icon). "
                         "Nothing changed; those changes need merging by hand first.")
    if dirty and git("stash", "pop", "--quiet", check=False).returncode:
        # (Skybound's own changes clash with the new ones: back to the installed source, with Skybound's changes)
        git("reset", "--hard", "--quiet", old)
        git("stash", "pop", "--quiet")
        raise SystemExit("The new GitHub changes touch the same lines as Skybound's own changes (input modes, icon). "
                         "The installed version stays; those changes need merging by hand first.")
    say("Source updated to " + git("log", "-1", "--format=%h %cd  %s", "--date=short").stdout.strip())


def toolchain():
    vswhere = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio/Installer/vswhere.exe"
    vcvars = None
    if vswhere.is_file():
        found = subprocess.run([str(vswhere), "-latest", "-products", "*", "-requires",
                                "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"],
                               capture_output=True, text=True).stdout.strip()
        if found:
            vcvars = Path(found) / "VC/Auxiliary/Build/vcvars64.bat"
    if not vcvars or not vcvars.is_file():
        raise SystemExit("Visual Studio Build Tools (C++) were not found. See README.md.")
    clang = shutil.which("clang")
    llvm = Path(clang).parent if clang else next((p for p in (Path(r"M:\LLVM\bin"), Path(r"C:\Program Files\LLVM\bin"))
                                                  if (p / "clang.exe").is_file()), None)
    if not llvm:
        raise SystemExit("LLVM (clang) was not found. See README.md.")
    return vcvars, llvm


def put_launcher():
    """Rebuilds launcher\\Skybound.exe and puts it in the package; the running launcher's exe is renamed out of the way
    (Windows lets a running program be renamed, not replaced)."""
    if subprocess.run(["cmd", "/c", str(ROOT / "launcher" / "build.bat")]).returncode:
        raise SystemExit("The launcher did not compile.")
    target = PACKAGE / "Skybound.exe"
    if target.is_file():
        old = PACKAGE / "Skybound.old.exe"
        try:
            old.unlink(missing_ok=True)
        except OSError:
            pass
        if not old.exists():
            target.rename(old)
    shutil.copy2(ROOT / "launcher" / "Skybound.exe", target)
    say("Launcher rebuilt.")


def rebuild(version, vcvars, llvm):
    suffix = "" if version == "1.0.0" else "-" + version
    game, build, dist = ROOT / ("game" + suffix), ROOT / ("build" + suffix), PACKAGE / "versions" / version
    if not (game / "exefs").is_dir():
        say(f"Skipping {version}: its game files ({game}) are missing.")
        return
    stamp = time.strftime("%Y%m%d-%H%M%S")
    old_build = build.with_name(f"{build.name}.old-{stamp}")
    old_dist = PACKAGE / "previous" / f"{version}-{stamp}"   # (outside versions\, so the launcher does not list it)
    say(f"\n== Rebuilding {version} (about 20 minutes)")
    if build.exists():
        build.rename(old_build)
    if dist.exists():
        old_dist.parent.mkdir(parents=True, exist_ok=True)
        dist.rename(old_dist)
    with tempfile.NamedTemporaryFile("w", suffix=".bat", delete=False) as bat:
        bat.write(f'@call "{vcvars}" >nul\r\n@set "PATH={llvm};%PATH%"\r\n'
                  f'@"{sys.executable}" -u "{ROOT / "build_game.py"}" --version {version}\r\n')
    try:
        result = subprocess.run(["cmd", "/c", bat.name], cwd=ROOT)
    finally:
        os.unlink(bat.name)
    if result.returncode:
        # (back to what was installed: the failed attempt's own outputs go, the old ones return)
        for path, old in ((dist, old_dist), (build, old_build)):
            if old.exists():
                if path.exists():
                    shutil.rmtree(path)
                old.rename(path)
        raise SystemExit(f"The {version} build failed; the previous {version} is still installed. See build.log.")
    if old_dist.exists():
        for name in KEEP:
            if (old_dist / name).exists():
                shutil.move(str(old_dist / name), str(dist / name))
        shutil.rmtree(old_dist)
    if old_build.exists():
        shutil.rmtree(old_build)
    say(f"{version} rebuilt; saves, mods and settings kept.")


def main():
    if "--no-pull" not in sys.argv:
        update_source()
    vcvars, llvm = toolchain()
    put_launcher()
    versions = sorted(d.name for d in (PACKAGE / "versions").iterdir() if (d / "SkyboundTOTK.exe").is_file()) \
        if (PACKAGE / "versions").is_dir() else []
    if not versions:
        say("No installed game versions to rebuild.")
    for version in versions:
        rebuild(version, vcvars, llvm)
    say("\nAll done.")


if __name__ == "__main__":
    main()
