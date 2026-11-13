"""Prove that the compiled city is faithful to the survey: packs, road graph, places and addresses.

The expectations are derived here, straight from the shapefiles, not through compile_city.py, so a
mistake in the compiler cannot hide in its own test.

Run against a test compile (about two minutes in all):
    .context/city/venv/bin/python Tools/city/compile_city.py --radius 700 --jobs 6
    .context/city/venv/bin/python Tools/city/roads.py
    .context/city/venv/bin/python Tools/city/places.py
    .context/city/venv/bin/python -m unittest discover -s Tools/city -v
A whole-comune pack (radius 0) is tested the same way; test groups whose files are missing are skipped.
"""
import functools
import json
import logging
import math
import re
import sys
import unittest
from collections import Counter
from pathlib import Path

import numpy as np
import shapefile
import shapely
from shapely.geometry import box
from shapely.strtree import STRtree

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT / "Tools/city"), str(ROOT / "Tools/geodata"), str(ROOT / "Tools/architecture")]
logging.getLogger("shapefile").setLevel(logging.ERROR)
import pack  # noqa: E402
import padova  # noqa: E402

CITY = ROOT / "Assets/StreamingAssets/City"
AREAS = ROOT / ".context/geodata/padova-dbt"
LINES = ROOT / ".context/city/dbt/linee/ComunePadova_linee"
POINTS = ROOT / ".context/city/dbt/punti/ComunePadova_punti"
OSM = ROOT / ".context/city/osm"
XZ_TOL = 0.002          # float32 keeps about 1 mm at 8 km from the origin
Y_TOL = 0.001
SIMPLIFY = 0.05         # compile_city.clean(): outlines are simplified by 5 cm, never moved further


def project(geoms):
    def f(coords):
        e, n = padova.PROJECT.transform(coords[:, 0], coords[:, 1])
        return np.column_stack([np.asarray(e) - padova.E0, np.asarray(n) - padova.N0])
    return shapely.transform(np.array(geoms, dtype=object), f)


def lonlat_box(extent, margin=400):
    x0, z0, x1, z1 = extent
    lon0, lat0 = padova.PROJECT.transform(x0 - margin + padova.E0, z0 - margin + padova.N0, direction="INVERSE")
    lon1, lat1 = padova.PROJECT.transform(x1 + margin + padova.E0, z1 + margin + padova.N0, direction="INVERSE")
    return lon0, lat0, lon1, lat1


def read_shapes(path, bbox=None):
    """[(row, attributes, local geometry)] straight from a shapefile."""
    reader = shapefile.Reader(str(path), encoding="latin1")
    fields = [f[0] for f in reader.fields[1:]]
    rows, geoms = [], []
    for i in range(len(reader)):
        s = reader.shape(i)
        if not s.points or bbox and (s.bbox[2] < bbox[0] or s.bbox[0] > bbox[2] or s.bbox[3] < bbox[1] or s.bbox[1] > bbox[3]):
            continue
        try:
            geoms.append(shapely.force_2d(shapely.geometry.shape(s.__geo_interface__)))
        except Exception:  # noqa: BLE001 - a malformed source shape has no geometry to compare
            continue
        rows.append(i)
    return [(i, dict(zip(fields, reader.record(i))), g) for i, g in zip(rows, project(geoms))] if geoms else []


@functools.lru_cache(maxsize=None)
def the_pack():
    return pack.read_city(CITY)


@functools.lru_cache(maxsize=None)
def emitted_units():
    out = {}
    for tile in the_pack()[1].values():
        for u in tile["units"]:
            out[(u["num"], u["part"])] = (u, tile)
    return out


@functools.lru_cache(maxsize=None)
def hero_ids():
    import plan_city
    core = json.loads((ROOT / "Assets/Geography/PadovaCentro/CityPlan.json").read_text())
    landmarks = json.loads((ROOT / "Assets/Art/Landmarks/Landmarks.json").read_text())
    return {u["id"] for u in core["units"]} | set(sum(plan_city.RAGIONE.values(), [])) | set(landmarks["replacedUnits"])


@functools.lru_cache(maxsize=None)
def survey_units():
    """{(num, part): record} for every surveyed volume part near the pack, with the documented exclusions marked.

    Mirrors the rules written in compile_city.load_units: UN_VOL needs 0 < AV < 150 and QGR > 0, minor
    volumes (MN_UVOL) 0.8 < ALT < 40 and QGR > 0; parts under 3 m2 are dropped; invalid source polygons
    are skipped (never repaired); hero units are drawn by their own models.
    """
    bbox = lonlat_box(the_pack()[0]["extent"])
    heroes, out = hero_ids(), {}
    for layer, prefix, qgr, height_field, lo, hi in (("UN_VOL", "UN_VOL", "UN_VOL_QGR", "UN_VOL_AV", 0.0, 150.0),
                                                     ("MN_UVOL", "MN_UVOL", "MN_UVO_QGR", "MN_UVO_ALT", 0.8, 40.0)):
        for row, attrs, geom in read_shapes(AREAS / layer, bbox):
            minor = layer == "MN_UVOL"
            num = (0x40000000 | row) if minor else int(attrs["OBJECTID"])
            ident = f"{prefix}:{row if minor else num}"
            height = attrs[height_field]
            excluded = None
            if geom.is_empty or not geom.is_valid:
                excluded = "invalid source polygon"
            elif not (lo < height < hi and attrs[qgr] > 0):
                excluded = "height filter"
            for part, poly in enumerate(padova.polygons(geom)):
                full = ident + ("" if part == 0 else f"#{part}")
                why = excluded or ("under 3 m2" if poly.area < 3 else "hero" if full in heroes else None)
                out[(num, part)] = {"id": full, "poly": poly, "top": attrs[qgr] - padova.VERTICAL_ORIGIN, "height": height,
                                    "inh": 0.0 if minor else attrs["UN_VOL_INH"], "por": "01" if minor else attrs["UN_VOL_POR"],
                                    "minor": minor, "excluded": why}
    return out


def needs(*paths):
    missing = [str(p.relative_to(ROOT)) for p in paths if not p.exists()]
    return unittest.skipIf(missing, "missing " + ", ".join(missing))


@needs(CITY / "city.json", AREAS / "UN_VOL.shp")
class PackIsFaithfulToTheSurvey(unittest.TestCase):
    def test_every_emitted_unit_is_a_surveyed_volume_that_should_be_there(self):
        survey = survey_units()
        self.assertGreater(len(emitted_units()), 1000)
        for key, (unit, _) in emitted_units().items():
            self.assertIn(key, survey, f"unit {key} is not in UN_VOL / MN_UVOL")
            self.assertIsNone(survey[key]["excluded"], f"{survey[key]['id']} is emitted but is excluded: {survey[key]['excluded']}")
            self.assertEqual(bool(unit["flags"] & pack.U_MINOR), survey[key]["minor"])

    def test_outlines_are_the_surveyed_outlines(self):
        """Every emitted vertex is a surveyed vertex of that record, and the outline as a whole stays
        within the 5 cm simplification of the surveyed one, with all its courtyards."""
        survey = survey_units()
        worst_vertex = worst_shape = 0.0
        for key, (unit, _) in emitted_units().items():
            source = survey[key]["poly"]
            self.assertEqual(len(unit["rings"]), 1 + len(source.interiors), f"{survey[key]['id']}: courtyards differ")
            source_xy = shapely.get_coordinates(source)
            for ring in unit["rings"]:
                d = np.hypot(ring[:, None, 0] - source_xy[None, :, 0], ring[:, None, 1] - source_xy[None, :, 1]).min(axis=1)
                worst_vertex = max(worst_vertex, float(d.max()))
            emitted = shapely.Polygon(unit["rings"][0], unit["rings"][1:])
            worst_shape = max(worst_shape, shapely.hausdorff_distance(emitted.boundary, source.boundary))
            self.assertLess(abs(emitted.area - source.area), 0.02 * source.area + 0.5, survey[key]["id"])
        self.assertLess(worst_vertex, XZ_TOL, "an emitted vertex is not a surveyed vertex")
        self.assertLess(worst_shape, SIMPLIFY + XZ_TOL, "an outline strays beyond the 5 cm simplification")

    def test_eave_base_and_portico_clearance_are_the_surveyed_values(self):
        survey, suspended = survey_units(), 0
        for key, (unit, _) in emitted_units().items():
            s = survey[key]
            self.assertAlmostEqual(unit["top"], s["top"], delta=Y_TOL, msg=f"{s['id']} eave")
            self.assertAlmostEqual(unit["base"], s["top"] - s["height"], delta=Y_TOL, msg=f"{s['id']} base")
            # A portico: a suspended portion (POR 02/03) whose surveyed clearance leaves a storey above it.
            expect = s["por"] in ("02", "03") and 2 < s["inh"] < s["height"] - 1.5
            self.assertEqual(bool(unit["flags"] & pack.U_SUSPENDED), expect, f"{s['id']} suspended flag")
            if expect:
                suspended += 1
                self.assertAlmostEqual(unit["under"] - unit["base"], s["inh"], delta=Y_TOL, msg=f"{s['id']} clearance")
            else:
                self.assertEqual(unit["under"], 0.0, f"{s['id']} has a clearance but is not a portico")
            self.assertGreaterEqual(unit["roofTop"], unit["top"] - Y_TOL, f"{s['id']} roof below its eave")
        self.assertGreater(suspended, 0, "the test area should contain porticoes")

    def test_no_surveyed_volume_is_silently_missing(self):
        """Inside the compiled area every surveyed part is in the pack unless a documented rule excludes it."""
        radius = the_pack()[0]["radius"]
        emitted, missing, excluded = emitted_units(), [], Counter()
        for key, s in survey_units().items():
            c = s["poly"].representative_point()
            if radius and math.hypot(c.x, c.y) >= radius:
                continue
            if s["excluded"]:
                excluded[s["excluded"]] += 1
            elif key not in emitted:
                missing.append(s["id"])
        print(f"\n  inside the compiled area: {dict(excluded)} excluded by rule", end=" ")
        self.assertEqual(missing, [], f"{len(missing)} surveyed volumes are missing")
        self.assertGreater(excluded["hero"], 0)

    def test_units_lie_in_the_tile_that_lists_them(self):
        tile_size = the_pack()[0]["tile"]
        for unit, tile in emitted_units().values():
            poly = shapely.Polygon(unit["rings"][0])
            rect = box(tile["tx"] * tile_size, tile["tz"] * tile_size, (tile["tx"] + 1) * tile_size, (tile["tz"] + 1) * tile_size)
            self.assertTrue(poly.intersects(rect), f"unit {unit['num']} is outside tile {tile['tx']},{tile['tz']}")

    def test_roofs_never_go_below_the_eave(self):
        units = [u for u, _ in emitted_units().values() if u["roof"] == 0]
        tree = STRtree([shapely.Polygon(u["rings"][0], u["rings"][1:]) for u in units])
        roofs = covered = 0
        for tile in the_pack()[1].values():
            for roof in tile["roofs"]:
                roofs += 1
                self.assertGreater(roof["slope"], 0.0)
                self.assertGreaterEqual(float(roof["nodes"][:, 2].min()), -1e-4, "a roof node lies below the eave plane")
                self.assertTrue(np.isfinite(roof["nodes"]).all())
                for face, _ in roof["faces"]:
                    self.assertLess(int(face.max()), len(roof["nodes"]))
                # The roof starts at the highest eave of the units it covers.
                inside = shapely.points(roof["nodes"][roof["faces"][0][0], :2].mean(axis=0))
                under = [units[i] for i in tree.query(inside, predicate="within") if abs(units[i]["roofTop"] - roof["top"]) < 1e-4]
                covered += bool(under)
                for u in under:
                    self.assertGreaterEqual(roof["top"], u["top"] - Y_TOL, f"roof below the eave of unit {u['num']}")
        self.assertGreater(roofs, 0)
        self.assertGreater(covered, 0.97 * roofs, "roofs could not be matched to the units they cover")

    def test_ground_patches_stay_inside_their_tile(self):
        meta, tiles = the_pack()
        size, patches = meta["tile"], 0
        for tile in tiles.values():
            x0, z0 = tile["tx"] * size, tile["tz"] * size
            for patch in tile["patches"]:
                patches += 1
                v = patch["vertices"]
                self.assertLess(patch["kind"], len(meta["kinds"]))
                self.assertLess(int(patch["triangles"].max()), len(v))
                self.assertTrue((v[:, 0] >= x0 - 0.02).all() and (v[:, 0] <= x0 + size + 0.02).all()
                                and (v[:, 1] >= z0 - 0.02).all() and (v[:, 1] <= z0 + size + 0.02).all(),
                                f"a {meta['kinds'][patch['kind']]} patch leaves tile {tile['tx']},{tile['tz']}")
        self.assertGreater(patches, 0)

    def test_tile_contents_are_well_formed(self):
        meta, tiles = the_pack()
        for tile in tiles.values():
            self.assertTrue(np.isfinite(tile["curbs"]).all())
            for unit in tile["units"]:
                self.assertLess(unit["cls"], len(meta["classes"]))
                n = sum(len(r) for r in unit["rings"])
                if unit["cap"] is not None:
                    self.assertEqual(len(unit["cap"]) % 3, 0)
                    self.assertLess(int(unit["cap"].max()), n)
                if unit["edgeFlags"] is not None:
                    self.assertEqual(len(unit["edgeFlags"]), n)
            for wall in tile["walls"]:
                self.assertGreaterEqual(len(wall["points"]), 2)
                self.assertTrue(0.4 <= wall["height"] <= 6)


@needs(CITY / "roads.bin", CITY / "roads.json")
class RoadGraph(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.graph = pack.read_roads(CITY / "roads.bin")
        cls.stats = json.loads((CITY / "roads.json").read_text())

    def test_counts_match_the_statistics(self):
        self.assertEqual(len(self.graph["nodes"]), self.stats["nodes"])
        self.assertEqual(len(self.graph["edges"]), self.stats["edges"])
        self.assertEqual(self.graph["names"][0], "")
        self.assertEqual(len(self.graph["names"]), len(set(self.graph["names"])))

    def test_edges_join_valid_nodes_and_start_and_end_on_them(self):
        nodes = self.graph["nodes"]
        for e in self.graph["edges"]:
            self.assertLess(max(e["a"], e["b"]), len(nodes))
            self.assertGreaterEqual(len(e["points"]), 2)
            self.assertEqual(tuple(e["points"][0]), (nodes["x"][e["a"]], nodes["z"][e["a"]]))
            self.assertEqual(tuple(e["points"][-1]), (nodes["x"][e["b"]], nodes["z"][e["b"]]))

    def test_attributes_are_in_range(self):
        speeds = Counter()
        for e in self.graph["edges"]:
            self.assertLess(e["name"], len(self.graph["names"]))
            self.assertLessEqual(e["cls"], 4)
            self.assertNotEqual(e["flags"] & 3, 3, "an edge is one-way in both directions")
            self.assertEqual(e["flags"] & 0xC0, 0, "undefined flag bits")
            self.assertTrue(1.0 <= e["width"] <= 30.0, f"width {e['width']}")
            self.assertTrue(1 <= e["lanes"] <= 12)
            self.assertTrue(5 <= e["maxspeed"] <= 150)
            if e["cls"] == 4:
                self.assertEqual(e["flags"] & 3, 0, "pedestrian edges carry no one-way")
            speeds[e["maxspeed"]] += 1
        self.assertGreater(speeds[50], speeds[130])

    def test_no_edge_is_degenerate(self):
        for e in self.graph["edges"]:
            p = e["points"].astype(float)
            length = float(np.hypot(*np.diff(p, axis=0).T).sum())
            self.assertGreater(length, 0.2, "a zero-length edge")
            self.assertTrue((np.hypot(*np.diff(p, axis=0).T) > 0).all(), "a repeated vertex")

    def test_degrees_and_dead_ends(self):
        nodes = self.graph["nodes"]
        degree = np.bincount(np.array([n for e in self.graph["edges"] for n in (e["a"], e["b"])]), minlength=len(nodes))
        self.assertTrue((degree > 0).all(), "a node without edges")
        self.assertTrue((np.minimum(degree, 255) == nodes["degree"]).all())
        self.assertTrue((nodes["kind"] <= 3).all())
        self.assertTrue((degree[nodes["kind"] == 3] == 1).all(), "a dead end with more than one edge")
        self.assertTrue((nodes["kind"][degree == 1] == 3).all(), "an unmarked dead end")

    def test_the_graph_is_one_connected_network(self):
        parent = list(range(len(self.graph["nodes"])))

        def find(i):
            while parent[i] != i:
                parent[i] = parent[parent[i]]
                i = parent[i]
            return i
        for e in self.graph["edges"]:
            parent[find(e["a"])] = find(e["b"])
        self.assertEqual(len({find(i) for i in range(len(parent))}), 1)
        self.assertGreater(self.stats["survey"]["components"]["mainShareOfLength"], 0.98)

    def test_one_ways_leave_nobody_stranded_inside_the_city(self):
        """Every node can be driven to and from (strong connectivity), except on the carriageways of fast roads
        that cross the comune without a junction: the A4 and the ring road enter and leave as one-way through-roads."""
        stranded = set(self.stats["osm"]["oneWayStubNodes"])
        self.assertEqual(len(stranded), self.stats["osm"]["nodesOutsideMainStrongComponent"])
        self.assertLessEqual(len(stranded), 0.03 * self.stats["nodes"])
        touching = [e for e in self.graph["edges"] if e["a"] in stranded or e["b"] in stranded]
        fast = sum(e["cls"] == 3 for e in touching)
        self.assertGreater(fast, 0.85 * len(touching), "stranded nodes away from the fast through-roads")

    @needs(LINES / "EL_STR.shp", AREAS / "AC_VEI.shp")
    def test_edges_run_on_the_surveyed_centrelines_and_carriageways(self):
        lines = [g for _, _, g in read_shapes(LINES / "EL_STR")]
        tree = STRtree(lines)
        samples, vertices = [], []
        for e in self.graph["edges"]:
            line = shapely.LineString(e["points"].astype(float))
            vertices.append(e["points"].astype(float))
            samples.append(shapely.get_coordinates(shapely.line_interpolate_point(line, np.arange(0.5, line.length, 5.0))))
        for pts, limit, what in ((np.vstack(vertices), 0.01, "vertex"), (np.vstack(samples), 0.2 + XZ_TOL, "simplified edge")):
            _, d = tree.query_nearest(shapely.points(pts), return_distance=True, all_matches=False)
            self.assertLess(float(d.max()), limit, f"a {what} is off the surveyed centreline")
        street = [g for name in ("AC_VEI", "AR_STR", "AC_PED", "A_PED") for _, _, g in read_shapes(AREAS / name) if g.is_valid]
        pts = shapely.points(np.vstack(samples))
        inside = np.zeros(len(pts), dtype=bool)
        inside[np.unique(STRtree(street).query(pts, predicate="within")[0])] = True
        self.assertGreater(inside.mean(), 0.985, "edges leave the surveyed street area")

    def test_street_names_read_naturally(self):
        import roads
        for raw, name in (("VIA SAN FRANCESCO", "Via San Francesco"), ("VIA SANTO (DEL)", "Via del Santo"),
                          ("PIAZZA ERBE (DELLE)", "Piazza delle Erbe"), ("VIA ARCO (DELL')", "Via dell'Arco"),
                          ("VIA TOMMASEO NICCOLO'", "Via Tommaseo Niccolò"), ("VIA PIETRO D'ABANO", "Via Pietro d'Abano"),
                          ("VIA D'ANNUNZIO GABRIELE", "Via D'Annunzio Gabriele"), ("VIA CA'MAGNO", "Via Ca' Magno"),
                          ("PONTE DI FERRO", "Ponte di Ferro"), ("VIA SAN PIO X", "Via San Pio X"),
                          ("VIA PELLEGRINO (BEATO)", "Via Beato Pellegrino"), ("PRATO DELLA VALLE", "Prato della Valle"),
                          ("VIABILITA' SENZA NOME", "")):
            self.assertEqual(roads.display_name(raw), name)
        names = set(self.graph["names"])
        self.assertIn("Prato della Valle", names)
        self.assertFalse([n for n in names if n.isupper() and len(n) > 3], "a name is still in registry capitals")


@needs(CITY / "places.json", CITY / "addresses.bin")
class PlacesAndAddresses(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.doc = json.loads((CITY / "places.json").read_text(encoding="utf-8"))
        cls.book = pack.read_addresses(CITY / "addresses.bin")

    def test_rows_are_complete_and_of_a_known_kind(self):
        for group, kinds in (("places", self.doc["placeKinds"]), ("pois", self.doc["poiKinds"])):
            self.assertGreater(len(self.doc[group]), 100)
            for row in self.doc[group]:
                self.assertTrue(row["name"].strip() and row["name"] == " ".join(row["name"].split()), row)
                self.assertIn(row["kind"], kinds, row)
                self.assertRegex(row["source"], r"^osm (node|way|relation) \d+$")
                self.assertTrue(abs(row["x"]) < 9000 and abs(row["z"]) < 9000, row)
                if group == "places":
                    self.assertIn(row["rank"], (1, 2, 3))
        self.assertGreater(len(self.doc["districts"]), 10)
        for row in self.doc["districts"]:
            self.assertTrue(row["name"] and row["kind"])

    def test_the_city_icons_are_there_once_each(self):
        icons = Counter(p["name"] for p in self.doc["places"] if p["rank"] == 1)
        for name in ("Basilica del Santo", "Prato della Valle", "Palazzo della Ragione", "Cappella degli Scrovegni",
                     "Duomo di Padova", "Palazzo del Bo", "La Specola", "Caffè Pedrocchi", "Orto Botanico",
                     "Stazione di Padova", "Stadio Euganeo"):
            self.assertEqual(icons[name], 1, name)
        ragione = next(p for p in self.doc["places"] if p["name"] == "Palazzo della Ragione")
        self.assertLess(math.hypot(ragione["x"], ragione["z"]), 150, "the origin of the local frame is beside the Palazzo")

    def test_no_landmark_is_listed_twice(self):
        seen = {}
        for p in self.doc["places"]:
            key = p["name"].lower()
            for q in seen.get(key, ()):
                self.assertGreater(math.hypot(p["x"] - q["x"], p["z"] - q["z"]), 400, f"{p['name']} is listed twice")
            seen.setdefault(key, []).append(p)

    @needs(OSM / "points.json", OSM / "areas.json", OSM / "buildings.json")
    def test_positions_are_the_openstreetmap_positions(self):
        where = {}
        for name in ("points", "areas", "buildings", "named"):
            if (OSM / f"{name}.json").exists():
                for e in json.loads((OSM / f"{name}.json").read_text())["elements"]:
                    pos = e.get("center") or (e if "lat" in e else None)
                    if pos:
                        where[f"osm {e['type']} {e['id']}"] = (pos["lon"], pos["lat"])
        for row in self.doc["places"] + self.doc["pois"] + self.doc["districts"]:
            x, z = padova.local(*where[row["source"]])
            # A way or relation has no single position: the downloads give a centre, which differs between snapshots.
            self.assertLess(math.hypot(row["x"] - x, row["z"] - z), 0.11 if row["source"].startswith("osm node") else 6.0, row)

    @needs(POINTS / "N_CIV.shp", AREAS / "TP_STR.shp")
    def test_addresses_are_the_surveyed_house_numbers(self):
        import roads
        names = roads.street_names()
        reader = shapefile.Reader(str(POINTS / "N_CIV"), encoding="latin1")
        lonlat = np.array([s.points[0] for s in reader.shapes()])
        east, north = padova.PROJECT.transform(lonlat[:, 0], lonlat[:, 1])
        expect = Counter()
        for i, r in enumerate(reader.records()):
            name = names.get(int(r["N_CIV_SCOD"]), "")
            if name:
                expect[(name, int(r["N_CIV_NUM"]), r["N_CIV_LETT"].strip().strip("-"),
                        round(float(east[i] - padova.E0), 1), round(float(north[i] - padova.N0), 1))] += 1
        table = self.book["names"]
        got = Counter((table[n], number, letters, round(float(np.float32(x)), 1), round(float(np.float32(z)), 1))
                      for x, z, n, number, letters in self.book["addresses"])
        self.assertEqual(sum(got.values()), self.doc["stats"]["addresses"]["addresses"])
        self.assertGreater(len(got), 50000)
        # Rounding to 0.1 m can fall either side of a float32 step; compare with a tolerance instead of exact keys.
        loose = lambda c: Counter((k[0], k[1], k[2]) for k in c.elements())   # noqa: E731
        self.assertEqual(loose(got), loose(expect))
        index = {}
        for k in expect:
            index.setdefault(k[:3], []).append(k[3:])
        for x, z, n, number, letters in self.book["addresses"][::37]:
            # Both sides are rounded to 0.1 m: up to 0.05 m per axis.
            self.assertLess(min(math.hypot(x - ex, z - ez) for ex, ez in index[(table[n], number, letters)]), 0.075)

    def test_addresses_are_sorted_and_well_formed(self):
        rows = [(n, number, letters) for _, _, n, number, letters in self.book["addresses"]]
        self.assertEqual(rows, sorted(rows))
        for n, number, letters in rows:
            self.assertTrue(0 < n < len(self.book["names"]) and number >= 1)
            self.assertTrue(len(letters) <= 6 and re.fullmatch(r"[A-Z]*", letters), letters)


if __name__ == "__main__":
    unittest.main(verbosity=2)
