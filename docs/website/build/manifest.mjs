/**
 * Which docs appear on the website, and where.
 * ---------------------------------------------------------------------------
 * This is the ONLY place you choose what gets published. Add or remove a line
 * and re-run `npm run build`.
 *
 * The Markdown in docs/*.md stays the single source of truth: titles, blurbs
 * and thumbnails are read out of each file automatically. You only override a
 * field here when the file itself cannot give a good answer — for example
 * CrossSections.md opens with the heading "Synopsis", which is a useless tile
 * title.
 *
 * Per-entry fields:
 *   file    (required)  path relative to docs/
 *   title   (optional)  overrides the document's first heading
 *   blurb   (optional)  overrides the document's first paragraph
 *   image   (optional)  overrides the document's first image; relative to docs/
 *   hide    (optional)  true = keep the page generated, drop it from the grid
 */

export const site = {
  repo: "https://github.com/pro3d-space/PRo3D",
  branch: "main",
  // Links to docs that are NOT published fall back to the source on GitHub.
};

export const sections = [
  {
    id: "features",
    eyebrow: "Features",
    title: "What PRo3D can do",
    intro:
      "Each card links to the full documentation for that feature — the same Markdown that ships with the source tree.",
    layout: "grid", // large tiles with thumbnails
    alt: true, // darker section background
    docs: [
      { file: "CrossSections.md", title: "Cross Sections" },
      { file: "Contour-Lines.md", title: "Contour Lines" },
      { file: "Feature-Multitexture.md", title: "Multitexturing" },
      { file: "GisView.md", title: "GIS View" },
      { file: "Transformations.md", title: "Transformations" },
      { file: "AdvancedAnnotations.md", title: "Advanced Annotations" },
      { file: "Feature-Queries.md", title: "Queries" },
      { file: "RIMFAXTraverse.md", title: "RIMFAX Traverses" },
      { file: "TraversePriorities.md", title: "Traverse Priorities" },
      { file: "spice.md", title: "SPICE Integration" },
      { file: "OpcTool.md", title: "OPC Tool" },
    ],
  },
  {
    id: "development",
    eyebrow: "Development",
    title: "Under the hood",
    intro:
      "Engineering notes for people working on PRo3D itself — architecture, tooling and the occasional post-mortem.",
    layout: "compact", // dense text tiles, no thumbnails
    alt: true,
    docs: [
      { file: "Build-Deploy-System.md", title: "Build & Deploy" },
      { file: "KdTrees.md", title: "KD-Trees & Picking" },
      { file: "ModelTypes.md", title: "Model Types & adaptify" },
      { file: "ProvenanceTracking.md", title: "Provenance Tracking" },
      {
        file: "story-picking-during-navigation.md",
        title: "Bug Story: Picking During Navigation",
      },
    ],
  },
];
