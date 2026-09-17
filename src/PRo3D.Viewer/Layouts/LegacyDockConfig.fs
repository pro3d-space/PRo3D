namespace PRo3D.Viewer

/// PRo3D <= 6.2 persisted its docking layout inside the scene as an FsPickler pickle of
/// `Aardvark.UI.Primitives.DockConfig`, and requires the `dockConfig` field when reading a
/// scene. Scenes keep carrying it so older versions can open them: a loaded scene writes
/// back what it read, a new scene writes this pickle of the 6.2 M2020 default layout.
/// It is a literal on purpose — the viewer no longer depends on the old docking types.
module LegacyDockConfig =

    let m2020 = """
{
  "FsPickler": "4.0.0",
  "type": "Aardvark.UI.Primitives.DockConfig",
  "value": {
    "content": {
      "Case": "Horizontal",
      "weight": 1.0,
      "children": [
        {
          "Case": "Stack",
          "weight": 0.7,
          "activeId": null,
          "children": [
            {
              "id": "render",
              "weight": 0.6,
              "title": {
                "Some": " Main View "
              },
              "deleteInvisible": null,
              "isCloseable": null
            }
          ]
        },
        {
          "Case": "Vertical",
          "weight": 0.3,
          "children": [
            {
              "Case": "Stack",
              "weight": 0.5,
              "activeId": {
                "Some": "surfaces"
              },
              "children": [
                {
                  "id": "surfaces",
                  "weight": 0.4,
                  "title": {
                    "Some": " Surfaces "
                  },
                  "deleteInvisible": null,
                  "isCloseable": null
                },
                {
                  "id": "annotations",
                  "weight": 0.4,
                  "title": {
                    "Some": " Annotations "
                  },
                  "deleteInvisible": null,
                  "isCloseable": null
                },
                {
                  "id": "scalebars",
                  "weight": 0.4,
                  "title": {
                    "Some": " ScaleBars "
                  },
                  "deleteInvisible": null,
                  "isCloseable": null
                },
                {
                  "id": "instrumentview",
                  "weight": 0.6,
                  "title": {
                    "Some": " Instrument View "
                  },
                  "deleteInvisible": null,
                  "isCloseable": null
                }
              ]
            },
            {
              "Case": "Stack",
              "weight": 0.5,
              "activeId": {
                "Some": "config"
              },
              "children": [
                {
                  "id": "config",
                  "weight": 0.4,
                  "title": {
                    "Some": " Config "
                  },
                  "deleteInvisible": null,
                  "isCloseable": null
                },
                {
                  "id": "bookmarks",
                  "weight": 0.4,
                  "title": {
                    "Some": " Bookmarks"
                  },
                  "deleteInvisible": null,
                  "isCloseable": null
                },
                {
                  "id": "sequencedBookmarks",
                  "weight": 0.4,
                  "title": {
                    "Some": " SequBookmarks "
                  },
                  "deleteInvisible": null,
                  "isCloseable": null
                },
                {
                  "id": "viewplanner",
                  "weight": 0.4,
                  "title": {
                    "Some": " Viewplans "
                  },
                  "deleteInvisible": null,
                  "isCloseable": null
                },
                {
                  "id": "properties",
                  "weight": 0.4,
                  "title": {
                    "Some": " Properties "
                  },
                  "deleteInvisible": null,
                  "isCloseable": null
                },
                {
                  "id": "traverse",
                  "weight": 0.4,
                  "title": {
                    "Some": " Traverses"
                  },
                  "deleteInvisible": null,
                  "isCloseable": null
                }
              ]
            }
          ]
        }
      ]
    },
    "specialDockSize": null,
    "appName": {
      "Some": "PRo3D"
    },
    "useCachedConfig": {
      "Some": false
    }
  }
}
"""

    let pickled () = m2020.Trim()
