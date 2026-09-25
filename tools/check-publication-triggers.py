#!/usr/bin/env python3
"""Fails the build if any workflow can publish a package from anything but a tag push.

ZabbixSender.Async 1.3 and earlier pushed to nuget.org on every push to master, so any commit
reaching the default branch became a package under a trusted name without a separate, deliberate
act. Publication is now tag-only (see .github/workflows/release.yml), and this gate keeps it so.

It lives outside .github/workflows on purpose: inlined in a workflow, the check's own pattern text
would match the file it is written in, and the gate would report a violation against itself.

The workflow is parsed rather than grepped. Triggers are read from the parsed 'on' mapping and
allowlisted, so a second trigger (pull_request, workflow_dispatch, a branch filter) cannot hide at
a different indentation. Publishing evidence is matched over the parsed document's scalars, with
shell continuations joined and whitespace collapsed, so a command split across lines is matched the
way bash reads it. Only scalars are scanned: a YAML comment mentioning a registry cannot publish to
it. A shell comment inside a run: block is part of that scalar and does match -- a false positive is
a build failure with the matched pattern printed next to it; a false negative is a permanent
publication.

Two residual gaps are deliberate. Publishing to a registry listed nowhere below is reported as
clean, so PATTERNS must be extended if the project ever ships to another registry. And a command
assembled at runtime from variables cannot be recognised by any static reader; that is left to
review.
"""

from __future__ import annotations

import glob
import re
import sys

try:
    import yaml
except ImportError:
    print("::error::PyYAML is required to evaluate publication triggers")
    raise SystemExit(1)

# Assembled from fragments so this file cannot match its own patterns if it is ever inlined into a
# workflow.
#
# Every token is lowercase; scalars are casefolded before matching. A group matches when all of its
# tokens appear in one scalar.
#
# NuGet/login is here because acquiring a publishing credential is the act worth noticing: under
# trusted publishing there is no stored key to grep for, and a workflow that exchanges an OIDC
# token for one is a publishing workflow whether or not it has yet reached the push. --api-key is
# the catch-all: it is how a credential is handed to the client, and it stays recognisable when
# the surrounding command does not. The legacy secret name is listed too, so a workflow reviving it
# is held to the same rules.
PATTERNS = (
    ("nuget" ".org",),
    ("nuget" "_api_key",),
    ("nuget" "apikey",),
    ("dotnet nuget", "push"),
    ("nuget/" "login",),
    ("--api" "-key",),
)
IMMUTABLE_ACTION_REFERENCE = re.compile(r"^[^@\s]+@[0-9a-f]{40}$")


def scalars(node: object):
    """Yields every scalar in the parsed document, keys included.

    Keys matter: an environment variable named after a publishing credential appears as a mapping
    key, not as a value.
    """
    if isinstance(node, dict):
        for key, value in node.items():
            yield from scalars(key)
            yield from scalars(value)
    elif isinstance(node, (list, tuple)):
        for item in node:
            yield from scalars(item)
    elif node is not None:
        yield str(node)


def normalise(text: str) -> str:
    """Renders a scalar the way the shell will read it, for matching."""
    return " ".join(text.replace("\\\n", " ").split()).casefold()


def publishing_evidence(document: object) -> str | None:
    """Returns the matched pattern group, or None if nothing in the document can publish."""
    for scalar in scalars(document):
        haystack = normalise(scalar)
        for group in PATTERNS:
            if all(token in haystack for token in group):
                return " + ".join(group)
    return None


def triggers(document: object) -> object:
    """Returns the 'on' mapping.

    YAML 1.1 resolves a bare 'on' key to the boolean True, so a loader returns True rather than
    'on'. Reading only one of the two spellings would make this gate blind to a real workflow.
    """
    if not isinstance(document, dict):
        return None

    for key in ("on", True):
        if key in document:
            return document[key]
    return None


def action_references(node: object):
    """Yields every action and reusable-workflow reference in the document."""
    if isinstance(node, dict):
        for key, value in node.items():
            if key == "uses" and isinstance(value, str):
                yield value
            else:
                yield from action_references(value)
    elif isinstance(node, (list, tuple)):
        for item in node:
            yield from action_references(item)


def check(path: str) -> list[str]:
    with open(path, encoding="utf-8") as handle:
        text = handle.read()

    # An unparseable workflow is not one this gate has cleared -- it is one it could not read, and
    # reporting that as clean would make "make the file unparseable" the cheapest bypass there is.
    try:
        document = yaml.safe_load(text)
    except yaml.YAMLError as error:
        return [f"{path} is not parseable YAML, so its triggers cannot be verified: {error}"]

    evidence = publishing_evidence(document)
    if evidence is None:
        return []

    print(f"{path} can publish packages ({evidence}); checking its triggers.")

    floating = [
        reference
        for reference in action_references(document)
        if not reference.startswith("./")
        and IMMUTABLE_ACTION_REFERENCE.fullmatch(reference) is None
    ]
    if floating:
        return [
            f"{path} publishes packages with external action '{reference}' that is not pinned "
            "to a full 40-character lowercase commit SHA"
            for reference in floating
        ]

    on = triggers(document)

    if on is None:
        return [f"{path} publishes packages but declares no triggers"]

    if isinstance(on, str):
        on = {on: None}
    if isinstance(on, list):
        on = dict.fromkeys(on)
    if not isinstance(on, dict):
        return [f"{path} publishes packages with an unrecognised trigger form"]

    extra = sorted(str(key) for key in on if key != "push")
    if extra:
        return [
            f"{path} publishes packages and also triggers on: {', '.join(extra)}. "
            "A tag push must be the only way to reach a publishing job."
        ]

    push = on.get("push")
    if not isinstance(push, dict):
        return [f"{path} publishes packages on every push, not only on tags"]

    filters = sorted(str(key) for key in push if key != "tags")
    if filters:
        return [
            f"{path} publishes packages with non-tag push filters: {', '.join(filters)}"
        ]

    if not push.get("tags"):
        return [f"{path} publishes packages with an empty tag filter"]

    return []


def main() -> int:
    workflows = sorted(
        glob.glob(".github/workflows/*.yml") + glob.glob(".github/workflows/*.yaml")
    )

    if not workflows:
        print("::error::no workflow files were found; this gate must never pass by scanning nothing")
        return 1

    failures = [failure for path in workflows for failure in check(path)]

    for failure in failures:
        print(f"::error::{failure}")

    if failures:
        return 1

    print(f"Publication triggers are tag-only across {len(workflows)} workflows.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
