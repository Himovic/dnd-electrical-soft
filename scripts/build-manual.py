#!/usr/bin/env python3
"""Builds the PDF user manuals from documentation/Manual/manual-<lang>.md.

Usage:  python3 scripts/build-manual.py [en] [fr]
Output: documentation/Manual/DND-Engineering-Suite-User-Manual-<LANG>.pdf
Requires: pip install reportlab pillow

Source format (subset of Markdown): a metadata block (key: value lines) ended by '---', '# ' chapters,
'## ' sections, '### ' subsections, paragraphs with **bold**, *italic* and `code`, '- ' and '1. ' lists,
pipe tables, fenced code blocks, '> ' notes and '![caption](figures/file.png)' figures.
"""
import os
import re
import sys
from datetime import date

from PIL import Image as PILImage
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (BaseDocTemplate, Frame, Image, KeepTogether, NextPageTemplate, PageBreak,
                                PageTemplate, Paragraph, Preformatted, Spacer, Table, TableStyle)
from reportlab.platypus.tableofcontents import TableOfContents

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
MANUAL = os.path.join(ROOT, "documentation", "Manual")
FONTS = os.path.join(ROOT, "resources", "Fonts")
SYS_FONTS = "/usr/share/fonts/truetype/dejavu"

pdfmetrics.registerFont(TTFont("DV", os.path.join(FONTS, "DejaVuSans.ttf")))
pdfmetrics.registerFont(TTFont("DV-B", os.path.join(FONTS, "DejaVuSans-Bold.ttf")))
mono = os.path.join(SYS_FONTS, "DejaVuSansMono.ttf")
pdfmetrics.registerFont(TTFont("DVM", mono if os.path.exists(mono) else os.path.join(FONTS, "DejaVuSans.ttf")))
pdfmetrics.registerFontFamily("DV", normal="DV", bold="DV-B", italic="DV", boldItalic="DV-B")

NAVY = colors.HexColor("#0F172A")
ACCENT = colors.HexColor("#2563EB")
GREY = colors.HexColor("#64748B")
LIGHT = colors.HexColor("#F1F5F9")
BORDER = colors.HexColor("#CBD5E1")

S = {
    "body": ParagraphStyle("body", fontName="DV", fontSize=9.5, leading=13.5, spaceAfter=5),
    "h1": ParagraphStyle("h1", fontName="DV-B", fontSize=20, leading=25, textColor=NAVY, spaceAfter=12),
    "h2": ParagraphStyle("h2", fontName="DV-B", fontSize=13.5, leading=18, textColor=ACCENT, spaceBefore=10, spaceAfter=5),
    "h3": ParagraphStyle("h3", fontName="DV-B", fontSize=10.5, leading=14, textColor=NAVY, spaceBefore=7, spaceAfter=3),
    "cell": ParagraphStyle("cell", fontName="DV", fontSize=8.2, leading=10.5),
    "cellh": ParagraphStyle("cellh", fontName="DV-B", fontSize=8.2, leading=10.5, textColor=colors.white),
    "code": ParagraphStyle("code", fontName="DVM", fontSize=8, leading=10.5, textColor=NAVY),
    "note": ParagraphStyle("note", fontName="DV", fontSize=9, leading=12.5, textColor=NAVY),
    "caption": ParagraphStyle("caption", fontName="DV", fontSize=8.5, leading=11, textColor=GREY, alignment=TA_CENTER, spaceAfter=8),
    "toc1": ParagraphStyle("toc1", fontName="DV-B", fontSize=10, leading=14, leftIndent=0),
    "toc2": ParagraphStyle("toc2", fontName="DV", fontSize=8.8, leading=11.5, leftIndent=14),
}


def inline(text):
    """Escapes XML and converts **bold**, *italic* and `code` to reportlab markup."""
    text = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    parts = re.split(r"(`[^`]+`)", text)
    out = []
    for p in parts:
        if p.startswith("`") and p.endswith("`") and len(p) > 1:
            out.append(f'<font name="DVM" color="#1E3A8A">{p[1:-1]}</font>')
        else:
            p = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", p)
            p = re.sub(r"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", r'<font color="#334155"><i>\1</i></font>', p)
            out.append(p)
    return "".join(out)


class ManualDoc(BaseDocTemplate):
    def __init__(self, path, meta, **kw):
        super().__init__(path, pagesize=A4, leftMargin=20 * mm, rightMargin=20 * mm, topMargin=22 * mm, bottomMargin=20 * mm,
                         title=meta["title"], author=meta["author"], subject=meta["subtitle"], creator="D&D Engineering Suite manual builder",
                         keywords=meta.get("keywords", ""), **kw)
        self.meta = meta
        frame = Frame(self.leftMargin, self.bottomMargin, self.width, self.height, id="f")
        self.addPageTemplates([PageTemplate("cover", [frame], onPage=self.cover),
                               PageTemplate("body", [frame], onPage=self.decorate)])

    def afterFlowable(self, f):
        if isinstance(f, Paragraph) and getattr(f, "toc_level", None) is not None:
            key = f"h{id(f)}"
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(f.plain, key, level=f.toc_level, closed=f.toc_level > 0)
            self.notify("TOCEntry", (f.toc_level, f.plain, self.page, key))

    def cover(self, c, doc):
        m = self.meta
        w, h = A4
        c.saveState()
        c.setFillColor(NAVY)
        c.rect(0, h - 120 * mm, w, 120 * mm, stroke=0, fill=1)
        c.drawImage(os.path.join(MANUAL, "figures", "logo.png"), 20 * mm, h - 62 * mm, 40 * mm, 40 * mm, mask="auto")
        c.setFillColor(colors.white)
        # Product name on one or two lines within the page width
        size, words, lines_ = 22, m["product"].split(), []
        for wd in words:
            if lines_ and pdfmetrics.stringWidth(lines_[-1] + " " + wd, "DV-B", size) <= w - 40 * mm:
                lines_[-1] += " " + wd
            else:
                lines_.append(wd)
        y = h - 76 * mm
        c.setFont("DV-B", size)
        for ln in lines_:
            c.drawString(20 * mm, y, ln)
            y -= 9 * mm
        c.setFont("DV", 13)
        c.drawString(20 * mm, y - 1 * mm, m["shortname"])
        c.setFont("DV", 11)
        c.setFillColor(colors.HexColor("#93C5FD"))
        c.drawString(20 * mm, h - 104 * mm, m["tagline"])
        c.setFillColor(NAVY)
        c.setFont("DV-B", 30)
        c.drawString(20 * mm, h - 150 * mm, m["title"])
        c.setFont("DV", 13)
        c.setFillColor(GREY)
        c.drawString(20 * mm, h - 162 * mm, m["subtitle"])
        y = h - 190 * mm
        c.setFont("DV", 10.5)
        c.setFillColor(NAVY)
        for label in ("version_label", "edition_label", "language_label"):
            c.drawString(20 * mm, y, m[label])
            y -= 7 * mm
        c.setStrokeColor(ACCENT)
        c.setLineWidth(2)
        c.line(20 * mm, 52 * mm, w - 20 * mm, 52 * mm)
        c.setFont("DV-B", 10.5)
        c.drawString(20 * mm, 42 * mm, m["author_line"])
        c.setFont("DV", 9.5)
        c.drawString(20 * mm, 35 * mm, m["copyright"])
        c.setFillColor(GREY)
        c.drawString(20 * mm, 28 * mm, m["powered"])
        c.restoreState()

    def decorate(self, c, doc):
        m = self.meta
        w, h = A4
        c.saveState()
        c.setFont("DV", 7.8)
        c.setFillColor(GREY)
        c.drawString(20 * mm, h - 13 * mm, f'{m["shortname"]} – {m["title"]}')
        c.drawRightString(w - 20 * mm, h - 13 * mm, m["version_label"])
        c.setStrokeColor(BORDER)
        c.setLineWidth(0.5)
        c.line(20 * mm, h - 15 * mm, w - 20 * mm, h - 15 * mm)
        c.line(20 * mm, 14 * mm, w - 20 * mm, 14 * mm)
        c.drawString(20 * mm, 9.5 * mm, m["copyright"])
        c.drawCentredString(w / 2, 5.5 * mm, m["powered"])
        c.drawRightString(w - 20 * mm, 9.5 * mm, f'{m["page"]} {doc.page}')
        c.restoreState()


def table(rows, width):
    ncol = max(len(r) for r in rows)
    rows = [r + [""] * (ncol - len(r)) for r in rows]
    lengths = [max(min(len(r[i]), 60) for r in rows) + 4 for i in range(ncol)]
    total = sum(lengths)
    widths = [width * l / total for l in lengths]
    data = [[Paragraph(inline(c), S["cellh"] if i == 0 else S["cell"]) for c in r] for i, r in enumerate(rows)]
    t = Table(data, colWidths=widths, repeatRows=1, hAlign="LEFT")
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), NAVY),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, LIGHT]),
        ("GRID", (0, 0), (-1, -1), 0.4, BORDER),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("TOPPADDING", (0, 0), (-1, -1), 3), ("BOTTOMPADDING", (0, 0), (-1, -1), 3),
        ("LEFTPADDING", (0, 0), (-1, -1), 4), ("RIGHTPADDING", (0, 0), (-1, -1), 4),
    ]))
    return t


def note(text, width):
    t = Table([[Paragraph(inline(text), S["note"])]], colWidths=[width])
    t.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, -1), colors.HexColor("#EFF6FF")),
                           ("LINEBEFORE", (0, 0), (0, -1), 3, ACCENT),
                           ("LEFTPADDING", (0, 0), (-1, -1), 8), ("TOPPADDING", (0, 0), (-1, -1), 5), ("BOTTOMPADDING", (0, 0), (-1, -1), 5)]))
    return t


def figure(path, caption, number, width, label):
    full = os.path.join(MANUAL, path)
    with PILImage.open(full) as im:
        iw, ih = im.size
    w = min(width, iw * 0.75)
    h = w * ih / iw
    if h > 150 * mm:
        h = 150 * mm
        w = h * iw / ih
    return KeepTogether([Spacer(1, 4), Image(full, w, h), Paragraph(inline(f"{label} {number} – {caption}"), S["caption"])])


def build(lang):
    src = os.path.join(MANUAL, f"manual-{lang}.md")
    lines = open(src, encoding="utf-8").read().splitlines()
    meta, i = {}, 0
    while lines[i].strip() != "---":
        if ":" in lines[i]:
            k, v = lines[i].split(":", 1)
            meta[k.strip()] = v.strip()
        i += 1
    meta["edition_label"] = meta["edition_label"].replace("{date}", meta.get("edition_date", date.today().isoformat()))
    lines = lines[i + 1:]
    out = os.path.join(MANUAL, f"DND-Engineering-Suite-User-Manual-{lang.upper()}.pdf")
    doc = ManualDoc(out, meta)
    width = doc.width
    story = [NextPageTemplate("body"), PageBreak()]

    toc = TableOfContents()
    toc.levelStyles = [S["toc1"], S["toc2"]]
    toc.dotsMinLevel = 0
    chapter = section = fig = 0
    para, items, ordered = [], [], False
    in_code, code = False, []
    table_rows = []

    def flush_para():
        nonlocal para
        if para:
            story.append(Paragraph(inline(" ".join(para)), S["body"]))
            para = []

    def flush_list():
        nonlocal items
        if items:
            for n, it in enumerate(items, 1):
                bullet = f"{n}." if ordered else "•"
                story.append(Paragraph(inline(it), ParagraphStyle("li", parent=S["body"], leftIndent=14, bulletIndent=3, spaceAfter=2.5, bulletFontName="DV"), bulletText=bullet))
            story.append(Spacer(1, 3))
            items = []

    def flush_table():
        nonlocal table_rows
        if table_rows:
            story.append(table(table_rows, width))
            story.append(Spacer(1, 7))
            table_rows = []

    def flush_all():
        flush_para(); flush_list(); flush_table()

    for raw in lines:
        line = raw.rstrip()
        if in_code:
            if line.startswith("```"):
                in_code = False
                box = Table([[Preformatted("\n".join(code), S["code"])]], colWidths=[width])
                box.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, -1), LIGHT), ("BOX", (0, 0), (-1, -1), 0.4, BORDER),
                                         ("LEFTPADDING", (0, 0), (-1, -1), 6), ("TOPPADDING", (0, 0), (-1, -1), 4), ("BOTTOMPADDING", (0, 0), (-1, -1), 4)]))
                story.append(box)
                story.append(Spacer(1, 6))
                code = []
            else:
                code.append(line)
            continue
        if line.startswith("```"):
            flush_all(); in_code = True; continue
        if line.strip() == "[[TOC]]":
            flush_all()
            story.append(Paragraph(meta["toc_title"], S["h1"]))
            story.append(toc)
            continue
        if line.strip() == "[[PAGEBREAK]]":
            flush_all(); story.append(PageBreak()); continue
        if line.startswith("# "):
            flush_all()
            chapter += 1; section = 0
            story.append(PageBreak())
            text = f"{chapter}. {line[2:].strip()}"
            p = Paragraph(inline(text), S["h1"]); p.toc_level = 0; p.plain = text
            story.append(p)
            continue
        if line.startswith("## "):
            flush_all()
            section += 1
            text = f"{chapter}.{section} {line[3:].strip()}"
            p = Paragraph(inline(text), S["h2"]); p.toc_level = 1; p.plain = text
            story.append(p)
            continue
        if line.startswith("### "):
            flush_all()
            story.append(Paragraph(inline(line[4:].strip()), S["h3"]))
            continue
        m = re.match(r"!\[(.+)\]\((.+)\)", line.strip())
        if m:
            flush_all(); fig += 1
            story.append(figure(m.group(2), m.group(1), fig, width, meta["figure"]))
            continue
        if line.startswith("> "):
            flush_all(); story.append(note(line[2:], width)); story.append(Spacer(1, 6)); continue
        if line.startswith("|"):
            flush_para(); flush_list()
            cells = [c.strip() for c in line.strip().strip("|").split("|")]
            if all(re.fullmatch(r":?-{2,}:?", c) for c in cells):
                continue
            table_rows.append(cells)
            continue
        if table_rows:
            flush_table()
        m = re.match(r"^(\s*)(- |\d+\. )(.*)", line)
        if m:
            flush_para()
            is_ordered = m.group(2)[0].isdigit()
            if items and is_ordered != ordered:
                flush_list()
            ordered = is_ordered
            items.append(m.group(3))
            continue
        if not line.strip():
            flush_para(); flush_list(); continue
        if items and line.startswith("  "):
            items[-1] += " " + line.strip(); continue
        para.append(line.strip())
    flush_all()
    doc.multiBuild(story)
    print(f"{out}: {doc.page} pages")


if __name__ == "__main__":
    for lang in (sys.argv[1:] or ["en", "fr"]):
        build(lang)
