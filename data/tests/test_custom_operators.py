import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_custom_operators import build_catalogue


def character(name, **changes):
    return {"name": name, "rarity": "TIER_6", "isNotObtainable": False,
            "nationId": "yan", "groupId": None, "teamId": None, "subPower": None,
            "position": "MELEE", "profession": "WARRIOR", "subProfessionId": "lord",
            "phases": [{}, {}, {"rangeId": "r", "attributesKeyFrames": [{"data": {"blockCnt": 2}}]}], **changes}


class CustomCatalogueTests(unittest.TestCase):
    def test_official_operator_missing_from_prts_is_reported(self):
        with self.assertRaisesRegex(ValueError, "char_new"):
            build_catalogue({}, [{"charId": "char_new", "isDiyChar": True}], {}, {})

    def test_filters_fixed_and_non_operators_but_keeps_alters_and_system_members(self):
        chars = {"char_1": character("普通自选"), "char_2": character("异格自选", isSpChar=True),
                 "char_3": character("固定"), "char_4": character("特殊", isNotObtainable=True),
                 "char_5": character("不可获取", isNotObtainable=True), "token_1": character("召唤物")}
        official = [{"charId": cid, "isDiyChar": cid != "char_3", "bonds": [{"name": "炎", "bondType": 1}]}
                    for cid in ("char_1", "char_2", "char_3", "char_4")]
        operators, profiles = build_catalogue(chars, official, {"lord": {"subProfessionName": "领主"}},
                                             {"r": {"grids": [{"col": 1, "row": 0}]}})
        self.assertEqual({o["name"] for o in operators}, {"普通自选", "异格自选", "特殊"})
        self.assertTrue(all(o["normal"]["traits"] == [] and o["elite"]["traits"] == [] for o in operators))
        self.assertIsNone(profiles["operators"]["异格自选"]["normalSkill"])
        self.assertEqual(profiles["ranges"]["r"], [[1, 0]])

    def test_official_covenants_override_character_archive_affiliations(self):
        chars = {"char_1": character("缄默德克萨斯", nationId="lungmen", subPower=[{"nationId": "siracusa"}]),
                 "char_2": character("焰影苇草", nationId="victoria")}
        official = [{"charId": "char_1", "isDiyChar": True, "bonds": [{"name": "叙拉古", "bondType": 1}]},
                    {"charId": "char_2", "isDiyChar": True, "bonds": [{"name": "协防干员", "bondType": 2}]}]
        operators, _ = build_catalogue(chars, official, {"lord": {"subProfessionName": "领主"}}, {"r": {"grids": []}})
        by_name = {o["name"]: o for o in operators}
        self.assertEqual(by_name["缄默德克萨斯"]["coreCovenants"], ["叙拉古"])
        self.assertEqual(by_name["焰影苇草"]["coreCovenants"], [])
        self.assertEqual(by_name["焰影苇草"]["additionalCovenants"], ["协防干员"])


if __name__ == "__main__":
    unittest.main()
