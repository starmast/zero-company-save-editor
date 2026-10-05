import os
import re

from zc import __version__

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def test_version_is_semver():
    assert re.fullmatch(r"\d+\.\d+\.\d+", __version__)


def test_changelog_documents_the_current_version():
    text = open(os.path.join(ROOT, "CHANGELOG.md"), encoding="utf-8").read()
    assert f"## [{__version__}]" in text
