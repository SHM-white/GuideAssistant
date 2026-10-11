#!/usr/bin/env python3
"""Validate the committed assembly version, MSIX identity and optional release tag."""
import argparse
import re
import sys
from pathlib import Path
from xml.etree import ElementTree

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--tag", help="vMAJOR.MINOR.PATCH release tag")
args = parser.parse_args()

project = ElementTree.parse(root / "GuideAssistant" / "GuideAssistant.csproj").getroot()
versions = [v.text.strip() for v in project.iter("Version") if v.text]
if len(versions) != 1:
    sys.exit("Expected exactly one project <Version> element.")
version = versions[0]
match = re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", version)
if not match or any(int(component) > 65535 for component in match.groups()):
    sys.exit(f"Invalid project version: {version}")

manifest = ElementTree.parse(root / "GuideAssistant" / "Package.appxmanifest").getroot()
identity = manifest.find("{http://schemas.microsoft.com/appx/manifest/foundation/windows10}Identity")
if identity is None:
    sys.exit("MSIX manifest Identity element is missing.")
msix_version = identity.get("Version")
if msix_version != f"{version}.0":
    sys.exit(f"MSIX version {msix_version!r} does not match project version {version}.0")
if args.tag and args.tag != f"v{version}":
    sys.exit(f"Tag {args.tag!r} does not match checked-out project version v{version}")
print(f"Version check passed: {version} / MSIX {msix_version}")
