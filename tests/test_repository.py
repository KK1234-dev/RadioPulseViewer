import json
import re
import unittest
import urllib.parse
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "RadioPulseViewer"
DATA_PATH = PROJECT / "Data" / "programs.json"
TIME_PATTERN = re.compile(r"^(\d{2}):(\d{2})$")
VALID_DAYS = {
    "Monday",
    "Tuesday",
    "Wednesday",
    "Thursday",
    "Friday",
    "Saturday",
    "Sunday",
}


def is_http_url(value: str) -> bool:
    parsed = urllib.parse.urlsplit(value)
    return parsed.scheme.lower() in {"http", "https"} and bool(parsed.netloc)


def is_broadcast_time(value: str) -> bool:
    match = TIME_PATTERN.fullmatch(value)
    if match is None:
        return False
    hours, minutes = map(int, match.groups())
    return 0 <= hours <= 47 and 0 <= minutes <= 59


class RepositoryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.catalog = json.loads(DATA_PATH.read_text(encoding="utf-8-sig"))

    def test_catalog_top_level_schema(self) -> None:
        self.assertEqual(
            {"lastReviewed", "dataNotice", "stations", "programs"},
            set(self.catalog),
        )
        self.assertRegex(self.catalog["lastReviewed"], r"^\d{4}-\d{2}-\d{2}$")
        self.assertTrue(self.catalog["dataNotice"].strip())
        self.assertGreater(len(self.catalog["stations"]), 0)
        self.assertGreater(len(self.catalog["programs"]), 0)

    def test_station_ids_and_urls(self) -> None:
        stations = self.catalog["stations"]
        station_ids = [station["id"] for station in stations]
        self.assertEqual(len(station_ids), len(set(station_ids)))

        for station in stations:
            with self.subTest(station=station["id"]):
                self.assertTrue(station["id"].strip())
                self.assertTrue(station["name"].strip())
                self.assertTrue(is_http_url(station["radikoUrl"]))
                self.assertTrue(is_http_url(station["officialScheduleUrl"]))

    def test_program_references_times_urls_and_unique_keys(self) -> None:
        station_ids = {station["id"] for station in self.catalog["stations"]}
        unique_keys: set[tuple[str, str, str, str]] = set()

        for index, program in enumerate(self.catalog["programs"]):
            with self.subTest(index=index, title=program.get("title")):
                self.assertIn(program["stationId"], station_ids)
                self.assertIn(program["day"], VALID_DAYS)
                self.assertTrue(program["title"].strip())
                self.assertTrue(is_broadcast_time(program["start"]))
                self.assertTrue(is_broadcast_time(program["end"]))
                self.assertTrue(is_http_url(program["programUrl"]))
                self.assertTrue(is_http_url(program["radikoUrl"]))

                key = (
                    program["stationId"],
                    program["day"],
                    program["start"],
                    program["title"],
                )
                self.assertNotIn(key, unique_keys)
                unique_keys.add(key)

    def test_xaml_and_project_files_are_well_formed(self) -> None:
        for relative_path in [
            "App.xaml",
            "MainWindow.xaml",
            "RadioPulseViewer.csproj",
        ]:
            with self.subTest(path=relative_path):
                ET.parse(PROJECT / relative_path)

    def test_project_targets_dotnet_10_wpf_and_webview2(self) -> None:
        project = ET.parse(PROJECT / "RadioPulseViewer.csproj").getroot()
        target_framework = project.findtext(".//TargetFramework")
        use_wpf = project.findtext(".//UseWPF")
        version = project.findtext(".//Version")
        authors = project.findtext(".//Authors")
        copyright_text = project.findtext(".//Copyright")
        package = project.find(".//PackageReference[@Include='Microsoft.Web.WebView2']")

        self.assertEqual("net10.0-windows", target_framework)
        self.assertEqual("true", use_wpf)
        self.assertEqual("1.0.0", version)
        self.assertEqual("Keisuke Katahira", authors)
        self.assertEqual("Copyright (c) 2026 Keisuke Katahira", copyright_text)
        self.assertIsNotNone(package)
        self.assertRegex(package.attrib["Version"], r"^\d+\.\d+\.\d+\.\d+$")

    def test_license_and_program_data_boundary_are_explicit(self) -> None:
        license_text = (ROOT / "LICENSE").read_text(encoding="utf-8")
        notice_text = (ROOT / "NOTICE.md").read_text(encoding="utf-8")

        self.assertIn("Copyright (c) 2026 Keisuke Katahira", license_text)
        self.assertIn("RadioPulseViewer/Data/programs.json", notice_text)
        self.assertIn("excluded from the MIT License", notice_text)


if __name__ == "__main__":
    unittest.main()
