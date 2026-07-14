/**
 * Renders docs/*.md into standalone HTML pages and injects tile grids into the
 * homepage. Run with `npm run build` (from docs/website).
 *
 * Pass --check to fail instead of writing when the output would change; used by
 * CI to catch a Markdown edit that was committed without a rebuild.
 *
 * Layout produced:
 *   docs/index.html               homepage, tiles injected
 *   docs/website/pages/*.html     one page per published document
 */

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import MarkdownIt from "markdown-it";
import { sections, site } from "./manifest.mjs";

const BUILD = path.dirname(fileURLToPath(import.meta.url));
const WEBSITE = path.resolve(BUILD, "..");
const DOCS = path.resolve(WEBSITE, "..");
const REPO = path.resolve(DOCS, "..");
const PAGES = path.join(WEBSITE, "pages");
const TEMPLATES = path.join(BUILD, "templates");

const CHECK = process.argv.includes("--check");

const warnings = [];
const warn = (m) => warnings.push(m);

/* ── helpers ─────────────────────────────────────────────────────────── */

/** Plain slug, used for heading anchors. "Dip & Strike" -> "dip-strike" */
const slugify = (s) =>
  s
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");

/**
 * Slug for a page filename, splitting camelCase so URLs stay readable:
 * "CrossSections.md" -> "cross-sections", "RIMFAXTraverse.md" -> "rimfax-traverse"
 */
const fileSlug = (s) =>
  slugify(
    s
      .replace(/\.md$/i, "")
      .replace(/([A-Z]+)([A-Z][a-z])/g, "$1-$2")
      .replace(/([a-z0-9])([A-Z])/g, "$1-$2")
  );

const escapeHtml = (s) =>
  s.replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c])
  );

const posix = (p) => p.split(path.sep).join("/");

/** Path from a generated page back to a file inside the repo. */
const relFromPages = (absTarget) => posix(path.relative(PAGES, absTarget));

const isExternal = (href) => /^(https?:)?\/\//i.test(href) || href.startsWith("mailto:");

/** GitHub source URL for a doc we did not publish. */
const githubUrl = (abs) =>
  `${site.repo}/blob/${site.branch}/${posix(path.relative(REPO, abs))}`;

/** Monogram for tiles with no image, e.g. "Cross Sections" -> "CS". */
const monogram = (title) =>
  title
    .replace(/[^A-Za-z0-9 ]/g, " ")
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0].toUpperCase())
    .join("");

/* ── read every entry up front so cross-links can resolve ────────────── */

/** absolute md path -> { slug, title, entry } */
const published = new Map();

for (const section of sections) {
  for (const entry of section.docs) {
    const abs = path.join(DOCS, entry.file);
    if (!fs.existsSync(abs)) {
      warn(`manifest lists a missing file: ${entry.file}`);
      continue;
    }
    published.set(abs, { slug: fileSlug(entry.file), entry, section });
  }
}

/* ── markdown-it, with rules that rewrite paths for the pages/ dir ───── */

const md = new MarkdownIt({ html: false, linkify: true, typographer: true });

/** Resolve a relative href/src written inside docs/<file>.md */
const resolveFromDocs = (target) => path.resolve(DOCS, target.replace(/^\.\//, ""));

md.renderer.rules.image = (tokens, idx, options, env, self) => {
  const token = tokens[idx];
  const src = token.attrGet("src");
  if (src && !isExternal(src)) {
    const abs = resolveFromDocs(src);
    if (!fs.existsSync(abs)) warn(`${env.file}: image not found -> ${src}`);
    token.attrSet("src", relFromPages(abs));
  }
  token.attrSet("loading", "lazy");
  return self.renderToken(tokens, idx, options);
};

md.renderer.rules.link_open = (tokens, idx, options, env, self) => {
  const token = tokens[idx];
  const href = token.attrGet("href") || "";

  if (isExternal(href)) {
    token.attrSet("target", "_blank");
    token.attrSet("rel", "noopener");
  } else if (href.startsWith("#")) {
    // in-page anchor, leave alone
  } else {
    const hashAt = href.indexOf("#");
    const file = hashAt === -1 ? href : href.slice(0, hashAt);
    const hash = hashAt === -1 ? "" : href.slice(hashAt);
    const abs = resolveFromDocs(file);
    const hit = published.get(abs);
    const insideDocs = !path.relative(DOCS, abs).startsWith("..");

    if (hit) {
      // Another published document -> its generated page.
      token.attrSet("href", `${hit.slug}.html${hash}`);
    } else if (insideDocs && fs.existsSync(abs)) {
      // An asset that ships with the site (an image, a PDF).
      token.attrSet("href", relFromPages(abs) + hash);
    } else {
      // Source code, an unpublished doc, or a dead link: send it to GitHub.
      // `#L69` line anchors keep working on a blob URL.
      if (!fs.existsSync(abs)) warn(`${env.file}: link target missing -> ${href}`);
      token.attrSet("href", githubUrl(abs) + hash);
      token.attrSet("target", "_blank");
      token.attrSet("rel", "noopener");
    }
  }
  return self.renderToken(tokens, idx, options);
};

/* ── extract title / blurb / thumbnail from a document ───────────────── */

const STRIP_LEAD = /^(synopsis|summary|tl;dr)\s*[:\-–]\s*/i;

function inspect(mdText, entry) {
  const tokens = md.parse(mdText, {});

  // first heading
  let heading = null;
  for (let i = 0; i < tokens.length; i++) {
    if (tokens[i].type === "heading_open" && tokens[i + 1]?.type === "inline") {
      heading = tokens[i + 1].content.trim();
      break;
    }
  }

  // first real paragraph (skip ones that are just an image)
  let blurb = "";
  for (let i = 0; i < tokens.length; i++) {
    if (tokens[i].type !== "paragraph_open") continue;
    const inline = tokens[i + 1];
    if (!inline || inline.type !== "inline") continue;
    const onlyImage = inline.children?.every(
      (c) => c.type === "image" || (c.type === "text" && !c.content.trim())
    );
    if (onlyImage) continue;
    // These docs write one logical statement per soft-wrapped line, e.g.
    //   Synopsis: Contour lines for texture layers
    //   Status: Work-In-Progress
    // so only the first line belongs in a tile blurb.
    const lines = inline.content.split("\n");
    blurb = lines[0]
      .replace(/^\s*>\s*/, "") // blockquote marker
      .replace(/!\[[^\]]*\]\([^)]*\)/g, "")
      .replace(/\[([^\]]*)\]\([^)]*\)/g, "$1") // keep link text, drop target
      .replace(/[*_`]/g, "") // emphasis + code fences, keep their contents
      .replace(/\s+/g, " ")
      .trim()
      .replace(STRIP_LEAD, "")
      .replace(/\s*:\s*$/, "");

    // The line was a hard wrap in the middle of a sentence, not the end of one.
    if (blurb && lines.length > 1 && !/[.!?)"'’”]$/.test(blurb)) blurb += "…";
    if (blurb) break;
  }
  if (blurb.length > 165) {
    blurb = blurb.slice(0, 165).replace(/\s+\S*$/, "") + "…";
  }

  // first image anywhere
  let image = null;
  outer: for (const t of tokens) {
    if (t.type !== "inline") continue;
    for (const c of t.children || []) {
      if (c.type === "image") {
        image = c.attrGet("src");
        break outer;
      }
    }
  }

  const title = entry.title || heading || path.basename(entry.file, ".md");
  return {
    title,
    blurb: entry.blurb || blurb,
    image: entry.image || image,
  };
}

/* ── heading anchors + table of contents ─────────────────────────────── */

function renderDoc(mdText, file, title) {
  let tokens = md.parse(mdText, {});
  const toc = [];
  const seen = new Map();

  // The page already shows `title` as its <h1>. If the document opens by
  // restating that title ("# Feature Contour Lines" under "Contour Lines"),
  // drop the heading. A first heading that is genuine content -- KdTrees.md
  // opens with "### General concept" -- is kept.
  if (tokens[0]?.type === "heading_open" && tokens[1]?.type === "inline") {
    const a = slugify(tokens[1].content.trim());
    const b = slugify(title);
    if (a && b && (a.includes(b) || b.includes(a))) tokens = tokens.slice(3);
  }

  // The page's <h1> is the manifest title, and the docs are inconsistent about
  // where they start (CrossSections.md opens at `#`, KdTrees.md at `###`).
  // Shift every heading so the shallowest one in the file becomes an <h2>.
  const levels = tokens
    .filter((t) => t.type === "heading_open")
    .map((t) => Number(t.tag.slice(1)));
  const delta = levels.length ? 2 - Math.min(...levels) : 0;
  const shift = (lvl) => Math.min(6, Math.max(2, lvl + delta));

  for (let i = 0; i < tokens.length; i++) {
    const t = tokens[i];
    if (t.type !== "heading_open") continue;
    const inline = tokens[i + 1];
    if (!inline || inline.type !== "inline") continue;

    const level = shift(Number(t.tag.slice(1)));
    t.tag = `h${level}`;
    if (tokens[i + 2]?.type === "heading_close") tokens[i + 2].tag = `h${level}`;

    const text = inline.content.trim();
    let id = slugify(text) || "section";
    if (seen.has(id)) {
      const n = seen.get(id) + 1;
      seen.set(id, n);
      id = `${id}-${n}`;
    } else {
      seen.set(id, 1);
    }
    t.attrSet("id", id);

    if (level === 2 || level === 3) toc.push({ id, text, level });
  }

  const html = md.renderer.render(tokens, md.options, { file });
  return { html, toc };
}

/* ── templates ───────────────────────────────────────────────────────── */

const tpl = (name) => fs.readFileSync(path.join(TEMPLATES, name), "utf8");
const fill = (t, vars) =>
  t.replace(/\{\{(\w+)\}\}/g, (_, k) => (k in vars ? vars[k] : ""));

/* ── tiles ───────────────────────────────────────────────────────────── */

function tileHtml(meta, slug, layout) {
  const href = `website/pages/${slug}.html`;
  const title = escapeHtml(meta.title);
  const blurb = escapeHtml(meta.blurb || "");

  let thumb = "";
  if (layout === "grid") {
    if (meta.image) {
      const src = isExternal(meta.image)
        ? meta.image
        : posix(path.relative(DOCS, resolveFromDocs(meta.image)));
      thumb = `<div class="tile-thumb"><img src="${escapeHtml(
        src
      )}" alt="" loading="lazy"></div>`;
    } else {
      thumb = `<div class="tile-thumb tile-thumb-empty" aria-hidden="true"><span>${escapeHtml(
        monogram(meta.title)
      )}</span></div>`;
    }
  }

  return `        <a class="tile reveal" href="${href}">
${thumb ? "          " + thumb + "\n" : ""}          <div class="tile-body">
            <h3>${title}</h3>
            ${blurb ? `<p>${blurb}</p>` : ""}
          </div>
          <span class="tile-arrow" aria-hidden="true">→</span>
        </a>`;
}

function sectionHtml(section, metas) {
  const tiles = section.docs
    .filter((e) => !e.hide && published.has(path.join(DOCS, e.file)))
    .map((e) => {
      const abs = path.join(DOCS, e.file);
      const { slug } = published.get(abs);
      return tileHtml(metas.get(abs), slug, section.layout);
    })
    .join("\n");

  const cls = section.layout === "compact" ? "tiles tiles-compact" : "tiles";
  const intro = section.intro
    ? `\n        <p class="section-sub">${escapeHtml(section.intro)}</p>`
    : "";

  return `  <section class="section${section.alt ? " section-alt" : ""}" id="${
    section.id
  }">
    <div class="wrap">
      <header class="section-head">
        <p class="eyebrow">${escapeHtml(section.eyebrow)}</p>
        <h2 class="section-title">${escapeHtml(section.title)}</h2>${intro}
      </header>

      <div class="${cls}">
${tiles}
      </div>
    </div>
  </section>`;
}

/* ── build ───────────────────────────────────────────────────────────── */

const outputs = new Map(); // abs path -> contents

const metas = new Map();
for (const [abs, { entry }] of published) {
  metas.set(abs, inspect(fs.readFileSync(abs, "utf8"), entry));
}

// 1. document pages
const docTpl = tpl("doc.html");
for (const [abs, { slug, section }] of published) {
  const meta = metas.get(abs);
  const source = fs.readFileSync(abs, "utf8");
  const { html, toc } = renderDoc(source, path.basename(abs), meta.title);

  const tocHtml = toc.length
    ? toc
        .map(
          (h) =>
            `<li class="toc-l${h.level}"><a href="#${h.id}">${escapeHtml(
              h.text
            )}</a></li>`
        )
        .join("\n            ")
    : "";

  // prev / next within the same section
  const siblings = section.docs
    .map((e) => path.join(DOCS, e.file))
    .filter((p) => published.has(p));
  const i = siblings.indexOf(abs);
  const link = (j, label) => {
    if (j < 0 || j >= siblings.length) return "";
    const s = published.get(siblings[j]);
    return `<a class="pager-${label}" href="${s.slug}.html"><span>${
      label === "prev" ? "Previous" : "Next"
    }</span>${escapeHtml(metas.get(siblings[j]).title)}</a>`;
  };

  outputs.set(
    path.join(PAGES, `${slug}.html`),
    fill(docTpl, {
      TITLE: escapeHtml(meta.title),
      DESCRIPTION: escapeHtml(meta.blurb || meta.title),
      SECTION: escapeHtml(section.eyebrow),
      SECTION_ID: section.id,
      CONTENT: html,
      TOC: tocHtml,
      TOC_CLASS: tocHtml ? "" : "is-empty", // collapses the sidebar column
      PREV: link(i - 1, "prev"),
      NEXT: link(i + 1, "next"),
      SOURCE_URL: githubUrl(abs),
      SOURCE_FILE: escapeHtml(posix(path.relative(REPO, abs))),
    })
  );
}

// 2. homepage
const indexTpl = tpl("index.html");
const vars = {};
for (const section of sections) {
  const key = `${section.id.toUpperCase()}_SECTION`;
  if (!indexTpl.includes(`{{${key}}}`)) {
    warn(`section "${section.id}" has no {{${key}}} placeholder in templates/index.html`);
  }
  vars[key] = sectionHtml(section, metas);
}
outputs.set(path.join(DOCS, "index.html"), fill(indexTpl, vars));

/* ── write (or verify) ───────────────────────────────────────────────── */

fs.mkdirSync(PAGES, { recursive: true });

// Remove pages for docs that are no longer published.
for (const f of fs.existsSync(PAGES) ? fs.readdirSync(PAGES) : []) {
  const abs = path.join(PAGES, f);
  if (f.endsWith(".html") && !outputs.has(abs)) {
    if (CHECK) {
      console.error(`stale page would be removed: ${posix(path.relative(REPO, abs))}`);
      process.exitCode = 1;
    } else {
      fs.unlinkSync(abs);
    }
  }
}

let changed = 0;
for (const [abs, content] of outputs) {
  const before = fs.existsSync(abs) ? fs.readFileSync(abs, "utf8") : null;
  if (before === content) continue;
  changed++;
  if (CHECK) {
    console.error(`out of date: ${posix(path.relative(REPO, abs))}`);
  } else {
    fs.mkdirSync(path.dirname(abs), { recursive: true });
    fs.writeFileSync(abs, content);
  }
}

for (const w of warnings) console.warn(`warning: ${w}`);

if (CHECK) {
  if (changed) {
    console.error(
      `\n${changed} file(s) out of date. Run \`npm run build\` in docs/website and commit the result.`
    );
    process.exitCode = 1;
  } else {
    console.log(`up to date — ${outputs.size} file(s) verified`);
  }
} else {
  console.log(
    `built ${published.size} doc page(s) + homepage; ${changed} file(s) written`
  );
}
