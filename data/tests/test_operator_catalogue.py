"""Current-season catalogue regression against the public official editor resource."""
import json
import re
import unittest
from pathlib import Path


DATA = Path(__file__).resolve().parents[1]
OFFICIAL = json.loads((DATA.parent / "src/HongxueAssistant.Tests/TestData/official_operators_v3_1.json").read_text(encoding="utf-8"))


class OfficialCatalogueTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.defaults = json.loads((DATA / "operators.json").read_text(encoding="utf-8-sig"))["operators"]
        cls.custom = json.loads((DATA / "custom_operators.json").read_text(encoding="utf-8-sig"))["operators"]
        cls.profiles = json.loads((DATA / "operator_profiles.json").read_text(encoding="utf-8-sig"))["operators"]

    def test_complete_default_and_custom_rosters(self):
        self.assertEqual({o["name"] for o in self.defaults},
                         {o["name"] for o in OFFICIAL["operators"] if not o["isCustom"]})
        self.assertEqual({o["sourceCharId"] for o in self.custom},
                         {o["charId"] for o in OFFICIAL["operators"] if o["isCustom"]})
        self.assertEqual(len(self.defaults) + len(self.custom), len(OFFICIAL["operators"]))

    def test_every_custom_covenant_matches_official_season(self):
        by_id = {o["sourceCharId"]: o for o in self.custom}
        for expected in OFFICIAL["operators"]:
            if not expected["isCustom"]:
                continue
            with self.subTest(name=expected["name"]):
                actual = by_id[expected["charId"]]
                self.assertEqual(sorted(actual["coreCovenants"]), sorted(expected["coreCovenants"]))
                self.assertEqual(sorted(actual["additionalCovenants"]), sorted(expected["additionalCovenants"]))

    def test_default_tiers_and_both_traits_match_official_season(self):
        by_name = {o["name"]: o for o in self.defaults}
        for expected in OFFICIAL["operators"]:
            if expected["isCustom"]:
                continue
            with self.subTest(name=expected["name"]):
                actual = by_name[expected["name"]]
                self.assertEqual(actual["tier"], expected["tier"])
                for variant in ("normal", "elite"):
                    self.assertEqual(
                        [re.sub(r"\s", "", t["traitDescription"]) for t in actual[variant]["traits"]],
                        [re.sub(r"\s", "", t["traitDescription"]) for t in expected[variant]["traits"]])

    def test_strategy_operator_has_prts_profile_and_no_dynamic_placeholders(self):
        support = self.profiles["盟约·辅助干员"]
        self.assertEqual(support["profession"], "辅助")
        self.assertEqual(support["subProfession"], "巫役")
        self.assertEqual(support["normalSkill"]["name"], "战术咏唱·双型")
        self.assertEqual(support["normalSkill"]["spCost"], 20)
        self.assertEqual(support["eliteSkill"]["spCost"], 17)
        for op in self.defaults:
            for variant in ("normal", "elite"):
                for trait in op[variant]["traits"]:
                    self.assertNotIn("{showtag_", trait["traitDescription"], op["name"])


if __name__ == "__main__":
    unittest.main()
