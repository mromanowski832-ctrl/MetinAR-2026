#!/usr/bin/env python3
"""Parser for Metin2-style text scripts (.msm model scripts, .msa motion scripts,
.mse effect scripts, .mss/.txt variants) -> JSON.

Grammar handled (generic, data only; nothing is executed):
    Key  value [value ...]            -> {"Key": value | [values]}
    Group Name  {  ...  }             -> {"Name": {...}}   (brace may be on the next line)
    List Name   {  item item ... }    -> {"Name": [items]}
Repeated keys/groups become lists. Quoted strings keep spaces. '#' and '//' start comments.

Usage: m2script.py FILE [FILE ...]   (prints JSON with parsed tree + referenced files)
"""
from __future__ import annotations

import json
import re
import shlex
import sys
from pathlib import Path

REF_EXT = (".gr2", ".dds", ".tga", ".png", ".jpg", ".mse", ".msa", ".msm", ".wav", ".mss")


class M2ScriptError(ValueError):
    pass


def _strip_comment(line: str) -> str:
    out, q = [], False
    i = 0
    while i < len(line):
        c = line[i]
        if c == '"':
            q = not q
        if not q and (c == "#" or line.startswith("//", i)):
            break
        out.append(c)
        i += 1
    return "".join(out).strip()


def _tokens(text: str):
    for lineno, raw in enumerate(text.splitlines(), 1):
        line = _strip_comment(raw)
        if not line:
            continue
        line = line.replace("{", " { ").replace("}", " } ")
        try:
            toks = shlex.split(line, posix=True)
        except ValueError as exc:
            raise M2ScriptError(f"line {lineno}: {exc}") from exc
        yield lineno, toks


def _number(tok: str):
    if re.fullmatch(r"[-+]?\d+", tok):
        return int(tok)
    if re.fullmatch(r"[-+]?(\d+\.\d*|\.\d+|\d+)([eE][-+]?\d+)?", tok):
        return float(tok)
    return tok


def _add(node: dict, key: str, value) -> None:
    if key in node:
        if not isinstance(node[key], list) or not getattr(node[key], "_multi", False):
            node[key] = _Multi([node[key]])
        node[key].append(value)
    else:
        node[key] = value


class _Multi(list):
    _multi = True


def parse_text(text: str) -> dict:
    lines = list(_tokens(text))
    root: dict = {}
    stack: list[tuple[str, object]] = [("root", root)]
    pending: tuple[str, str] | None = None  # (kind, name) waiting for '{'
    for lineno, toks in lines:
        i = 0
        while i < len(toks):
            t = toks[i]
            kind, cur = stack[-1]
            if pending is not None:
                if t != "{":
                    raise M2ScriptError(f"line {lineno}: expected '{{' after {pending[0]} {pending[1]}")
                new = [] if pending[0] == "List" else {}
                if kind == "list":
                    raise M2ScriptError(f"line {lineno}: block inside List")
                _add(cur, pending[1], new)  # type: ignore[arg-type]
                stack.append(("list" if pending[0] == "List" else "group", new))
                pending = None
                i += 1
                continue
            if t == "}":
                if len(stack) == 1:
                    raise M2ScriptError(f"line {lineno}: unmatched '}}'")
                stack.pop()
                i += 1
                continue
            if kind == "list":
                cur.append(_number(t))  # type: ignore[union-attr]
                i += 1
                continue
            if t in ("Group", "List") and i + 1 < len(toks):
                pending = (t, toks[i + 1])
                i += 2
                continue
            values = [_number(v) for v in toks[i + 1:] if v not in ("{", "}")]
            closing = toks[i + 1:].count("}")
            _add(cur, t, values[0] if len(values) == 1 else values)  # type: ignore[arg-type]
            for _ in range(closing):
                stack.pop()
            break
    if pending is not None:
        raise M2ScriptError(f"unterminated {pending[0]} {pending[1]}")
    if len(stack) != 1:
        raise M2ScriptError("unterminated block (missing '}')")
    return root


def referenced_files(tree) -> list[str]:
    found: list[str] = []

    def walk(v):
        if isinstance(v, dict):
            for x in v.values():
                walk(x)
        elif isinstance(v, list):
            for x in v:
                walk(x)
        elif isinstance(v, str) and v.lower().endswith(REF_EXT):
            found.append(v.replace("\\", "/"))

    walk(tree)
    return sorted(set(found), key=found.index)


def summarize(tree: dict) -> dict:
    """Small typed summary for .msm/.msa so manifests can link model <-> clips."""
    st = str(tree.get("ScriptType", ""))
    s: dict = {"script_type": st}
    if "BaseModelFileName" in tree:
        s["base_model"] = tree["BaseModelFileName"]
    if "MotionFileName" in tree:
        s["motion_file"] = tree["MotionFileName"]
        s["duration_s"] = tree.get("MotionDuration")
        s["accumulation"] = tree.get("Accumulation")
    s["references"] = referenced_files(tree)
    return s


def parse_file(path: str | Path) -> dict:
    raw = Path(path).read_bytes()
    for enc in ("utf-8", "cp949", "cp1250", "latin-1"):
        try:
            text = raw.decode(enc)
            break
        except UnicodeDecodeError:
            continue
    tree = parse_text(text)
    return {"path": str(path), "encoding": enc, "tree": tree, "summary": summarize(tree)}


def main(argv: list[str]) -> int:
    if not argv:
        print(__doc__)
        return 2
    out, rc = [], 0
    for f in argv:
        try:
            out.append(parse_file(f))
        except (OSError, M2ScriptError) as exc:
            out.append({"path": f, "error": str(exc)})
            rc = 1
    print(json.dumps(out, indent=2, ensure_ascii=False))
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
