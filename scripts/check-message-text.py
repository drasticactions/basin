#!/usr/bin/env python3
# Checks the text of messages and the shape of catch blocks under src/ and samples/.
#
# A message is a string literal in a log call, a thrown exception, an IPC error
# reply, a standard-error write or a known UI setter, or a XAML text attribute.
# A message must not hold a semicolon, an em dash or " -- ". A catch block must
# not be empty, must not log only {e.Message}, and must not throw a new exception
# built from e.Message without passing e as the inner exception.
#
# A string that a method returns through an "out string" parameter is a message
# too when a caller sends that parameter as the text of an IPC error reply. Such
# a string must be a capitalized sentence. The check follows the out variable
# from reply.Error back to the call that set it, then to the method and to the
# methods that it passes the parameter on to. It reports each assignment of a
# literal that starts with a lowercase letter.
#
# Every site prints as "path:line: kind: text", then a count per project. The
# exit status is 1 when a site is in an enforced project and is not allowed.
#
# Usage: scripts/check-message-text.sh [--project NAME]... [--quiet] [PATH...]

import argparse
import html
import os
import re
import sys
from collections import Counter

# Every project is enforced. To bring a new tree in step by step, set
# ENFORCE_ALL to False and name the finished projects in ENFORCED.
ENFORCED = set()

ENFORCE_ALL = True

# Kept sites, as (path suffix, fragment of the reported text). The reason for
# each one is in the spec for the text and exception cleanup.
ALLOWED = [
    ("samples/BasinCtl/Controller.cs", "empty catch block"),
    ("samples/BasinMcp/McpSocketWatch.cs", "empty catch block"),
    ("src/Basin.Ipc.Client/BasinIpcClient.cs", "empty catch block"),
    ("src/Basin.Ipc.Client/IpcEventWait.cs", "empty catch block"),
    ("src/Basin.Ipc.Client/IpcLauncher.cs", "empty catch block"),
    ("src/Basin.Transport.Waypipe/WslVm.cs", "wsl.exe -- wslinfo --vm-id"),
]

BAD_TEXT = re.compile(r";|—| -- ")

LOG_CALL = re.compile(r"\b(?:\w*[Ll]og\w*|BasinLog)\s*\.\s*(?:Trace|Debug|Info|Warn|Error)\s*\(")
THROW_CALL = re.compile(r"\bthrow\s+new\s+[\w.]*Exception\s*\(")
REPLY_CALL = re.compile(r"\b\w*[Rr]eply\s*\.\s*Error\s*\(")
STDERR_CALL = re.compile(r"\bConsole\s*\.\s*Error\s*\.\s*Write(?:Line)?(?:Async)?\s*\(")
UI_SETTER = re.compile(r"(?<![\w.])(?:\w+\s*\.\s*)?(?:Status|Refusal|Note|Label|StatusText|Watermark)\s*=(?!=)")
XAML_ATTR = re.compile(r"\b(?:Text|Content|Title|Header|Watermark|ToolTip\.Tip)\s*=\s*\"([^\"]*)\"")
CATCH = re.compile(r"\bcatch\b")


def lex(text):
    """Yields (kind, start, end, literal) for code. Kinds: str, comment, char, code."""
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        if text.startswith("//", i):
            j = text.find("\n", i)
            j = n if j < 0 else j
            yield ("comment", i, j, None)
            i = j
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            yield ("comment", i, j, None)
            i = j
            continue
        if c == "'":
            j = i + 1
            while j < n and text[j] != "'":
                j += 2 if text[j] == "\\" else 1
            yield ("char", i, j + 1, None)
            i = j + 1
            continue
        m = re.match(r"(\$+)?(@)?(\$+)?\"", text[i:i + 8])
        if m and (c in "$@\""):
            end, literal = read_string(text, i)
            yield ("str", i, end, literal)
            i = end
            continue
        yield ("code", i, i + 1, None)
        i += 1


def read_string(text, i):
    n = len(text)
    j = i
    dollars = 0
    verbatim = False
    while text[j] in "$@":
        if text[j] == "$":
            dollars += 1
        else:
            verbatim = True
        j += 1
    quotes = 0
    while j + quotes < n and text[j + quotes] == '"':
        quotes += 1
    if quotes >= 3:
        return read_raw(text, j, quotes, dollars)
    j += 1
    out = []
    while j < n:
        c = text[j]
        if c == '"':
            if verbatim and text.startswith('""', j):
                out.append('"')
                j += 2
                continue
            return j + 1, "".join(out)
        if c == "\\" and not verbatim:
            out.append(text[j:j + 2])
            j += 2
            continue
        if dollars and c == "{":
            if text.startswith("{{", j):
                out.append("{")
                j += 2
                continue
            j = skip_hole(text, j)
            out.append("{}")
            continue
        if dollars and text.startswith("}}", j):
            out.append("}")
            j += 2
            continue
        if c == "\n" and not verbatim:
            return j, "".join(out)
        out.append(c)
        j += 1
    return n, "".join(out)


def read_raw(text, j, quotes, dollars):
    n = len(text)
    fence = '"' * quotes
    start = j + quotes
    k = start
    out = []
    while k < n:
        if text.startswith(fence, k):
            return k + quotes, "".join(out)
        if dollars and text.startswith("{" * dollars, k) and not text.startswith("{" * (dollars + 1), k):
            k = skip_hole(text, k + dollars - 1)
            out.append("{}")
            continue
        out.append(text[k])
        k += 1
    return n, "".join(out)


def skip_hole(text, j):
    depth = 0
    for kind, start, end, _ in lex_from(text, j):
        if kind != "code":
            continue
        c = text[start]
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return end
    return len(text)


def lex_from(text, j):
    sub = text[j:]
    for kind, start, end, literal in lex(sub):
        yield kind, start + j, end + j, literal


class Source:
    def __init__(self, path, text):
        self.path = path
        self.text = text
        self.tokens = list(lex(text))
        self.code_mask = bytearray(len(text))
        self.starts = {}
        for index, (kind, start, end, _) in enumerate(self.tokens):
            self.starts[start] = index
            if kind == "code":
                for k in range(start, end):
                    self.code_mask[k] = 1
        self.lines = [0]
        for k, c in enumerate(text):
            if c == "\n":
                self.lines.append(k + 1)

    def line(self, offset):
        lo, hi = 0, len(self.lines)
        while lo + 1 < hi:
            mid = (lo + hi) // 2
            if self.lines[mid] <= offset:
                lo = mid
            else:
                hi = mid
        return lo + 1

    def is_code(self, offset):
        return 0 <= offset < len(self.code_mask) and self.code_mask[offset] == 1

    def token_index_at(self, offset):
        while offset not in self.starts and offset > 0:
            offset -= 1
        return self.starts.get(offset, 0)

    def literals_until(self, offset, stop):
        """Collects literals from offset to the close of the call, or to the end of the statement."""
        depth = 0
        found = []
        index = self.token_index_at(offset)
        for kind, start, end, literal in self.tokens[index:]:
            if start < offset:
                continue
            if kind == "str":
                found.append(literal)
                continue
            if kind != "code":
                continue
            c = self.text[start]
            if c in "([{":
                depth += 1
            elif c in ")]}":
                depth -= 1
                if stop == "call" and depth == 0:
                    return found, end
                if depth < 0:
                    return found, end
            elif c == ";" and depth <= 0:
                return found, end
        return found, len(self.text)


def message_sites(source):
    shapes = [("log", LOG_CALL, "call"), ("throw", THROW_CALL, "call"), ("ipc", REPLY_CALL, "call"),
              ("stderr", STDERR_CALL, "call"), ("ui", UI_SETTER, "statement")]
    for kind, pattern, stop in shapes:
        for match in pattern.finditer(source.text):
            if not source.is_code(match.start()):
                continue
            begin = match.end() - 1 if stop == "call" else match.end()
            literals, _ = source.literals_until(begin, stop)
            for literal in literals:
                if BAD_TEXT.search(literal):
                    yield source.line(match.start()), kind, literal


def catch_sites(source):
    text = source.text
    for match in CATCH.finditer(text):
        if not source.is_code(match.start()):
            continue
        k = match.end()
        name = None
        head = re.compile(r"\s*\(\s*[\w.<>]+(?:\s+(\w+))?\s*\)").match(text, k)
        if head:
            name = head.group(1)
            k = head.end()
        when = re.compile(r"\s*when\s*\(").match(text, k)
        if when:
            depth = 0
            j = when.end() - 1
            while j < len(text):
                if source.is_code(j):
                    if text[j] == "(":
                        depth += 1
                    elif text[j] == ")":
                        depth -= 1
                        if depth == 0:
                            break
                j += 1
            k = j + 1
        brace = re.compile(r"\s*\{").match(text, k)
        if not brace:
            continue
        open_at = brace.end() - 1
        depth = 0
        j = open_at
        while j < len(text):
            if source.is_code(j):
                if text[j] == "{":
                    depth += 1
                elif text[j] == "}":
                    depth -= 1
                    if depth == 0:
                        break
            j += 1
        body = text[open_at + 1:j]
        line = source.line(match.start())
        if body.strip() == "":
            yield line, "catch-empty", "empty catch block"
            continue
        if not name:
            continue
        for call in LOG_CALL.finditer(body):
            call_text = body[call.start():body.find(";", call.start()) + 1]
            if re.search(r"\{\s*" + name + r"\s*\.\s*Message\b", call_text):
                yield line, "catch-message", call_text.strip()[:120]
        for throw in THROW_CALL.finditer(body):
            stop = body.find(";", throw.start())
            call_text = body[throw.start():stop + 1]
            if re.search(r"\b" + name + r"\s*\.\s*Message\b", call_text) and not re.search(r"[,(]\s*" + name + r"\s*\)", call_text):
                yield line, "catch-wrap", call_text.strip()[:120]


REPLY_VARIABLE = re.compile(r"\.\s*Error\s*\(\s*IpcErrorCodes\s*\.\s*\w+\s*,\s*(\w+)\b")
OUT_ARGUMENT = re.compile(r"\bout\s+(?:var\s+|string\??\s+)?(\w+)\s*[,)]")
TYPE_DECLARATION = re.compile(r"\b(?:class|struct|record|interface)\s+(\w+)")
OUT_PARAMETER = re.compile(r"^\s*(?:\[[^\]]*\]\s*)*out\s+string\??\s+(\w+)\s*$")
NOT_METHODS = {"if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "return",
               "nameof", "typeof", "sizeof", "default", "when", "new", "base", "this", "checked", "unchecked"}
REPLY_REACH = 1500


def close_of(source, open_at, opening, closing):
    depth = 0
    k = open_at
    text = source.text
    while k < len(text):
        if source.is_code(k):
            if text[k] == opening:
                depth += 1
            elif text[k] == closing:
                depth -= 1
                if depth == 0:
                    return k
        k += 1
    return len(text)


def split_arguments(source, open_at, close_at):
    parts = []
    depth = 0
    start = open_at + 1
    text = source.text
    for k in range(open_at + 1, close_at):
        if not source.is_code(k):
            continue
        if text[k] in "([{":
            depth += 1
        elif text[k] in ")]}":
            depth -= 1
        if text[k] == "," and depth == 0:
            parts.append((start, k))
            start = k + 1
    parts.append((start, close_at))
    return parts


class MethodIndex:
    """Indexes the method bodies of every source, by name."""

    def __init__(self, sources):
        self.by_name = {}
        self.types = set()
        for source in sources:
            for match in TYPE_DECLARATION.finditer(source.text):
                if source.is_code(match.start()):
                    self.types.add(match.group(1))
            self.index(source)

    def index(self, source):
        text = source.text
        for match in re.finditer(r"\b([A-Za-z_]\w*)\s*(?:<[\w\s,?]*>)?\s*\(", text):
            name = match.group(1)
            if name in NOT_METHODS or not source.is_code(match.start()):
                continue
            before = text[max(0, match.start() - 400):match.start()].rstrip()
            if not before or not re.search(r"[\w>?\]]$", before) or before.endswith(("new", "return", "await", "else", "in", "is", "as", "throw", "case")):
                continue
            if re.search(r"[.=(,!&|+\-*/:]\s*$", before):
                continue
            open_at = match.end() - 1
            close_at = close_of(source, open_at, "(", ")")
            rest = text[close_at + 1:close_at + 200]
            body = re.match(r"\s*(?:where\s[^{;]*)?\{", rest)
            if not body:
                continue
            declaration_start = max(before.rfind(";"), before.rfind("{"), before.rfind("}"))
            modifiers = before[declaration_start + 1:]
            parameters = [text[a:b] for a, b in split_arguments(source, open_at, close_at)]
            if not any(OUT_PARAMETER.match(parameter) for parameter in parameters):
                continue
            body_open = close_at + 1 + body.end() - 1
            body_close = close_of(source, body_open, "{", "}")
            entry = {
                "source": source,
                "name": name,
                "static": re.search(r"\bstatic\b", modifiers) is not None,
                "private": re.search(r"\b(?:public|internal|protected)\b", modifiers) is None,
                "type": self.enclosing_type(source, match.start()),
                "parameters": parameters,
                "body": (body_open, body_close),
            }
            self.by_name.setdefault(name, []).append(entry)

    @staticmethod
    def enclosing_type(source, offset):
        best = None
        for match in TYPE_DECLARATION.finditer(source.text, 0, offset):
            if source.is_code(match.start()):
                brace = source.text.find("{", match.end())
                if 0 <= brace < offset and close_of(source, brace, "{", "}") > offset:
                    best = match.group(1)
        return best

    def targets(self, source, call):
        """Gives the methods that a call can reach, from its receiver."""
        name, receiver, index = call
        found = []
        for entry in self.by_name.get(name, []):
            parameters = entry["parameters"]
            if index >= len(parameters) or not OUT_PARAMETER.match(parameters[index]):
                continue
            if receiver is None:
                if entry["source"] is not source:
                    continue
            elif receiver in self.types:
                if not entry["static"] or entry["type"] != receiver:
                    continue
            else:
                if entry["static"] or (entry["private"] and entry["source"] is not source):
                    continue
            found.append(entry)
        return found


def call_of(source, offset):
    """Gives (name, receiver, argument index) of the call whose argument list holds offset."""
    text = source.text
    depth = 0
    k = offset
    while k > 0:
        k -= 1
        if not source.is_code(k):
            continue
        if text[k] in ")]}":
            depth += 1
        elif text[k] in "([{":
            if depth == 0:
                if text[k] != "(":
                    return None
                break
            depth -= 1
    else:
        return None
    open_at = k
    head = re.search(r"(?:([A-Za-z_]\w*)\s*[!?]?\s*\.\s*)?([A-Za-z_]\w*)\s*$", text[max(0, open_at - 200):open_at])
    if not head:
        return None
    close_at = close_of(source, open_at, "(", ")")
    index = 0
    for number, (a, b) in enumerate(split_arguments(source, open_at, close_at)):
        if a <= offset <= b:
            index = number
            break
    receiver = head.group(1)
    if receiver in ("this", "base"):
        receiver = None
    return head.group(2), receiver, index


def reply_out_sites(sources):
    """Yields (source, line, literal) for each lowercase literal that an IPC error reply can show."""
    index = MethodIndex(sources)
    work = []
    for source in sources:
        for reply in REPLY_VARIABLE.finditer(source.text):
            if not source.is_code(reply.start()):
                continue
            variable = reply.group(1)
            window = source.text[max(0, reply.start() - REPLY_REACH):reply.start()]
            outs = [m for m in OUT_ARGUMENT.finditer(window) if m.group(1) == variable]
            if not outs:
                continue
            offset = max(0, reply.start() - REPLY_REACH) + outs[-1].start()
            if not source.is_code(offset):
                continue
            call = call_of(source, offset)
            if call:
                work.append((source, call))
    seen = set()
    reported = set()
    while work:
        source, call = work.pop()
        for entry in index.targets(source, call):
            key = (entry["source"].path, entry["body"][0])
            if key in seen:
                continue
            seen.add(key)
            target = entry["source"]
            parameter = OUT_PARAMETER.match(entry["parameters"][call[2]]).group(1)
            begin, end = entry["body"]
            text = target.text
            assignment = re.compile(r"\b" + re.escape(parameter) + r"\s*(?:\?\?)?=(?!=)\s*")
            for match in assignment.finditer(text, begin, end):
                if not target.is_code(match.start()):
                    continue
                token = target.starts.get(match.end())
                if token is None:
                    continue
                kind, start, _, literal = target.tokens[token]
                if kind == "str" and literal and literal[0].islower() and (target.path, start) not in reported:
                    reported.add((target.path, start))
                    yield target, target.line(match.start()), literal
            passing = re.compile(r"\bout\s+" + re.escape(parameter) + r"\s*[,)]")
            for match in passing.finditer(text, begin, end):
                if target.is_code(match.start()):
                    inner = call_of(target, match.start())
                    if inner:
                        work.append((target, inner))


def xaml_sites(path, text):
    for match in XAML_ATTR.finditer(text):
        value = match.group(1)
        if value.startswith("{"):
            continue
        value = html.unescape(value)
        if BAD_TEXT.search(value):
            yield text.count("\n", 0, match.start()) + 1, "xaml", value


def project_of(path):
    parts = path.split(os.sep)
    if len(parts) >= 2 and parts[0] in ("src", "samples", "apps"):
        return parts[1]
    return parts[0]


def allowed(path, text):
    return any(path.endswith(suffix) and fragment in text for suffix, fragment in ALLOWED)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", action="append", default=[])
    parser.add_argument("--quiet", action="store_true")
    parser.add_argument("paths", nargs="*")
    args = parser.parse_args()

    root = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
    os.chdir(root)
    roots = args.paths or [r for r in ("src", "samples", "apps") if os.path.isdir(r)]

    sources = {}
    for top in [r for r in ("src", "samples", "apps") if os.path.isdir(r)]:
        for directory, dirs, files in os.walk(top):
            dirs[:] = sorted(d for d in dirs if d not in ("bin", "obj"))
            for file in sorted(files):
                if file.endswith(".cs"):
                    path = os.path.join(directory, file)
                    with open(path, encoding="utf-8-sig") as handle:
                        sources[path] = Source(path, handle.read())
    out_sites = {}
    for source, line, literal in reply_out_sites(list(sources.values())):
        out_sites.setdefault(source.path, []).append((line, "ipc-out", literal))

    counts = Counter()
    failures = 0
    for top in roots:
        for directory, dirs, files in os.walk(top):
            dirs[:] = sorted(d for d in dirs if d not in ("bin", "obj"))
            for file in sorted(files):
                path = os.path.join(directory, file)
                project = project_of(path)
                if args.project and project not in args.project:
                    continue
                if file.endswith(".cs"):
                    source = sources.get(path)
                    if source is None:
                        with open(path, encoding="utf-8-sig") as handle:
                            source = Source(path, handle.read())
                    sites = list(message_sites(source)) + list(catch_sites(source)) + out_sites.get(path, [])
                elif file.endswith((".axaml", ".xaml")):
                    with open(path, encoding="utf-8-sig") as handle:
                        text = handle.read()
                    sites = list(xaml_sites(path, text))
                else:
                    continue
                for line, kind, literal in sorted(sites):
                    if allowed(path, literal):
                        continue
                    counts[project] += 1
                    enforced = ENFORCE_ALL or project in ENFORCED
                    if enforced:
                        failures += 1
                    if not args.quiet or enforced:
                        flat = literal.replace("\n", " ")
                        print(f"{path}:{line}: {kind}: {flat}")

    if counts:
        print()
        for project, count in sorted(counts.items(), key=lambda item: (-item[1], item[0])):
            mark = " (enforced)" if ENFORCE_ALL or project in ENFORCED else ""
            print(f"{count:5} {project}{mark}")
        print(f"{sum(counts.values()):5} total")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
