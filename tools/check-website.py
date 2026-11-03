"""Checks the static website in web/: balanced HTML, unique ids, local links and in-page anchors.

Usage: python tools/check-website.py [web-root]     (exit code 1 on any problem)
Standard library only, so it runs on any CI runner.
"""
import html.parser
import os
import sys

VOID = {"area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
        # SVG elements written as <x .../>
        "path", "circle", "rect", "stop", "line", "polyline", "polygon", "ellipse", "use"}


class Page(html.parser.HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.stack, self.errors, self.ids, self.links = [], [], set(), []
        self.lang = None

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if tag == "html":
            self.lang = a.get("lang")
        if "id" in a:
            if a["id"] in self.ids:
                self.errors.append(f"duplicate id '{a['id']}'")
            self.ids.add(a["id"])
        if tag == "img" and "alt" not in a:
            self.errors.append(f"<img src='{a.get('src')}'> has no alt")
        for key in ("href", "src"):
            if a.get(key):
                self.links.append(a[key])
        if a.get("srcset"):
            self.links += [part.strip().split(" ")[0] for part in a["srcset"].split(",")]
        if tag not in VOID:
            self.stack.append((tag, self.getpos()[0]))

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in VOID:
            self.stack.pop()

    def handle_endtag(self, tag):
        if tag in VOID:
            return
        if self.stack and self.stack[-1][0] == tag:
            self.stack.pop()
        else:
            open_tag = self.stack[-1] if self.stack else None
            self.errors.append(f"line {self.getpos()[0]}: </{tag}> but open is {open_tag}")


def target(root, link):
    path = link.split("#")[0].split("?")[0]
    if path.endswith("/"):
        path += "index.html"
    return os.path.join(root, path.lstrip("/").replace("/", os.sep))


def main():
    root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "web"))
    pages, problems = {}, 0
    for folder, _, files in os.walk(root):
        for name in files:
            if name.endswith(".html"):
                path = os.path.join(folder, name)
                page = Page()
                page.feed(open(path, encoding="utf-8").read())
                pages[path] = page

    for path, page in sorted(pages.items()):
        rel = os.path.relpath(path, root)
        errors = list(page.errors)
        if page.stack:
            errors.append(f"unclosed tags: {page.stack}")
        if not page.lang:
            errors.append("<html> has no lang")
        for link in page.links:
            if link.startswith("#"):
                if link[1:] and link[1:] not in page.ids:
                    errors.append(f"anchor {link} not found")
            elif link.startswith("/") and not link.startswith("//"):
                dest = target(root, link)
                if not os.path.exists(dest):
                    errors.append(f"broken link {link}")
                elif "#" in link and dest in pages:
                    anchor = link.split("#", 1)[1]
                    if anchor and anchor not in pages[dest].ids:
                        errors.append(f"anchor {link} not found")
        for e in errors:
            print(f"{rel}: {e}")
        problems += len(errors)

    print(f"{len(pages)} pages checked, {problems} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
