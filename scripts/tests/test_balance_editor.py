#!/usr/bin/env python3
"""数值管理器（scripts/tools/balance_editor.py）的服务端逻辑测试。

抓的静默错误：
1. `check_shape` 的键集双向校验失效——编辑器能往 balance.json 写进界面上看不见的键，或合法保存被误拒；
2. `render_save` 的行尾处理回归——一次保存把整个文件的行尾翻掉，git diff 被淹没；
3. `restore_numeric_kinds` 失效——`1.0` 被写成 `1`，同上；
4. 保存/回滚的备份语义走样：结构不符时仍然落盘、无改动也写文件、回滚不留现场。

这些都不会报错、只会悄悄坏（文件看起来正常），且只能在文件层面看出来，故在此钉住。
"""

from __future__ import annotations

import copy
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))
import balance_editor as editor  # noqa: E402
import balance_analysis as analysis  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
BALANCE = REPO / "data" / "balance.json"


def load_balance() -> dict:
    return json.loads(BALANCE.read_text(encoding="utf-8"))


class CheckShapeTests(unittest.TestCase):
    """键集双向 + 标量类型 + 数组元素模板。"""

    def test_missing_and_unknown_keys_both_rejected(self):
        old = {"a": {"b": 1}}
        self.assertEqual([], editor.check_shape({"a": {"b": 2}}, old))
        self.assertTrue(editor.check_shape({"a": {}}, old))                     # 缺键
        self.assertTrue(editor.check_shape({"a": {"b": 1}, "c": 1}, old))       # 多键
        self.assertTrue(editor.check_shape({"a": {"b": 1, "z": 9}}, old))       # 对象内多键

    def test_scalar_types(self):
        self.assertTrue(editor.check_shape({"a": "1"}, {"a": 1}))
        self.assertTrue(editor.check_shape({"a": 1}, {"a": "1"}))
        self.assertTrue(editor.check_shape({"a": 1}, {"a": True}))   # bool 是 int 子类，必须拒
        self.assertTrue(editor.check_shape({"a": True}, {"a": 1}))
        self.assertEqual([], editor.check_shape({"a": True}, {"a": False}))

    def test_numeric_array_length_is_free_but_element_type_is_not(self):
        old = {"a": [1, 2, 3]}
        self.assertEqual([], editor.check_shape({"a": [9]}, old))
        self.assertEqual([], editor.check_shape({"a": [1, 2, 3, 4]}, old))
        self.assertTrue(editor.check_shape({"a": [1, "x"]}, old))

    def test_object_array_elements_use_own_position_as_template(self):
        # boss 的阶段表按位置形状不同（有的带 waves、有的带 duration）：
        # 一律拿 old[0] 当模板会把原样文件判成缺键，保存被整体拒掉，编辑器等于不可用
        old = {"p": [{"waves": 1}, {"duration": 2.0}]}
        self.assertEqual([], editor.check_shape(copy.deepcopy(old), old))
        self.assertEqual([], editor.check_shape({"p": [{"waves": 1}]}, old))    # 元素个数可变（界面能删元素）
        self.assertTrue(editor.check_shape({"p": [{"waves": 1}, {"duration": 2.0, "x": 1}]}, old))


class DiffTests(unittest.TestCase):
    def test_numeric_array_is_one_leaf(self):
        diff = editor.diff_values({"a": [1, 2, 3]}, {"a": [1, 2]})
        self.assertEqual(1, len(diff))
        self.assertEqual("a", diff[0]["path"])

    def test_object_array_descends_per_element(self):
        diff = editor.diff_values({"p": [{"x": 1}, {"x": 5}]}, {"p": [{"x": 1}, {"x": 2}]})
        self.assertEqual([{"path": "p[1].x", "old": 2, "new": 5}], diff)

    def test_int_float_sameness_is_not_a_change(self):
        # JSON 往返后 10.0 会变成 10：判成差异会让「没动过任何键」也报出一串改动
        self.assertEqual([], editor.diff_values({"a": 10}, {"a": 10.0}))

    def test_no_change_on_identical_document(self):
        balance = load_balance()
        self.assertEqual([], editor.diff_values(copy.deepcopy(balance), balance))


class RenderSaveTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp()) / "balance.json"
        self.addCleanup(shutil.rmtree, self.tmp.parent, ignore_errors=True)

    def test_lf_file_stays_lf(self):
        self.tmp.write_bytes(b'{\n\t"a": 1\n}\n')
        text = editor.render_save({"a": 2}, self.tmp)
        self.assertNotIn("\r", text)
        self.assertTrue(text.endswith("}\n"))
        self.assertIn('\t"a": 2', text)

    def test_crlf_file_stays_crlf(self):
        # 只换末尾那一个换行不够：正文的 LF 若原样写出，文件会变成混合行尾
        self.tmp.write_bytes(b'{\r\n\t"a": 1\r\n}\r\n')
        text = editor.render_save({"a": 2, "b": 3}, self.tmp)
        self.assertEqual(text.count("\n"), text.count("\r\n"))   # 没有裸 LF
        self.assertTrue(text.endswith("}\r\n"))


class RestoreNumericKindsTests(unittest.TestCase):
    def test_float_stays_float_and_int_stays_int(self):
        old = {"f": 1.0, "i": 1, "arr": [2.0, 3]}
        new = {"f": 1, "i": 1, "arr": [2, 3]}
        out = editor.restore_numeric_kinds(new, old)
        self.assertIsInstance(out["f"], float)
        self.assertIsInstance(out["i"], int)
        self.assertIsInstance(out["arr"][0], float)
        self.assertIsInstance(out["arr"][1], int)

    def test_changed_value_keeps_its_own_kind(self):
        out = editor.restore_numeric_kinds({"f": 2}, {"f": 1.0})
        self.assertIsInstance(out["f"], int)

    def test_real_file_round_trip_preserves_values_and_kinds(self):
        # 端到端：原样文档过一遍「归一 + 序列化 + 反解析」后，值与每个叶子的数值类型都不变
        # ——「只改一处却带出成片 1.0 → 1」正是从这一层漏出去的
        balance = load_balance()
        text = editor.render_save(editor.restore_numeric_kinds(copy.deepcopy(balance), balance), BALANCE)
        again = json.loads(text)
        self.assertEqual(balance, again)

        def kinds(node):
            if isinstance(node, dict):
                for value in node.values():
                    yield from kinds(value)
            elif isinstance(node, list):
                for value in node:
                    yield from kinds(value)
            elif analysis.is_num(node):
                yield type(node)

        self.assertEqual(list(kinds(balance)), list(kinds(again)))

    def test_render_is_idempotent(self):
        # 同一份内容渲染两次必须逐字相同：否则「保存一次」与「保存两次」的 diff 不同，仓库会漂
        balance = load_balance()
        once = editor.render_save(balance, BALANCE)
        self.assertEqual(once, editor.render_save(json.loads(once), BALANCE))


class EditorSaveTests(unittest.TestCase):
    def setUp(self):
        self.dir = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.dir, ignore_errors=True)
        self.path = self.dir / "balance.json"
        shutil.copy(BALANCE, self.path)
        self.editor = editor.Editor(self.path)

    def test_save_writes_and_backs_up(self):
        before = self.path.read_text(encoding="utf-8")
        payload = copy.deepcopy(self.editor.read_balance())
        payload["player"]["max_speed"] = 430
        code, body = self.editor.save(payload)
        self.assertEqual(200, code)
        self.assertEqual([{"path": "player.max_speed", "old": 420, "new": 430}], body["changes"])
        self.assertEqual(before, self.editor.backup.read_text(encoding="utf-8"))
        self.assertEqual(430, self.editor.read_balance()["player"]["max_speed"])

    def test_shape_error_does_not_touch_file(self):
        before = self.path.read_text(encoding="utf-8")
        payload = copy.deepcopy(self.editor.read_balance())
        payload["player"]["not_a_real_key"] = 1
        code, body = self.editor.save(payload)
        self.assertEqual(400, code)
        self.assertFalse(body["ok"])
        self.assertEqual(before, self.path.read_text(encoding="utf-8"))
        self.assertFalse(self.editor.backup.exists())   # 校验不过不该留下备份

    def test_unchanged_document_is_not_rewritten(self):
        code, body = self.editor.save(copy.deepcopy(self.editor.read_balance()))
        self.assertEqual(200, code)
        self.assertEqual([], body["changes"])
        self.assertFalse(self.editor.backup.exists())

    def test_readonly_refuses_save_and_revert(self):
        editor_ro = editor.Editor(self.path, readonly=True)
        payload = copy.deepcopy(editor_ro.read_balance())
        payload["player"]["max_speed"] = 431
        self.assertEqual(400, editor_ro.save(payload)[0])
        self.assertEqual(400, editor_ro.revert()[0])

    def test_revert_without_backup_is_refused(self):
        code, body = self.editor.revert()
        self.assertEqual(400, code)
        self.assertIn("没有备份", body["message"])

    def test_revert_restores_backup_and_keeps_scene(self):
        payload = copy.deepcopy(self.editor.read_balance())
        payload["player"]["max_speed"] = 500
        self.editor.save(payload)
        code, body = self.editor.revert()
        self.assertEqual(200, code)
        self.assertEqual(420, self.editor.read_balance()["player"]["max_speed"])
        self.assertTrue(self.path.with_suffix(".json.pre-revert").exists())


class StateEndpointTests(unittest.TestCase):
    def test_state_carries_meta_for_real_paths(self):
        state = editor.Editor(BALANCE).state()
        self.assertIn("player.max_speed", state["meta"])
        self.assertEqual("最大速度", state["meta"]["player.max_speed"]["title"])
        self.assertIn("player", state["sections"])
        # 模板 `enemies.types[].hp` 必须落到具体下标上，前端才能按叶子路径取到说明
        self.assertIn("enemies.types.0.hp", state["meta"])
        self.assertEqual(len(state["balance"]["enemies"]["types"]),
                         sum(1 for key in state["meta"] if key.startswith("enemies.types.") and key.endswith(".hp")))


class ExpandPathsTests(unittest.TestCase):
    def test_wildcards_and_prefix(self):
        tree = {"a": {"t1": {"x": 1}, "t2": {"x": 2}}, "arr": [{"v": 1}, {"v": 2}], "early_hold": 1, "early_max": 2}
        self.assertEqual(["a.t1.x", "a.t2.x"], sorted(analysis.expand_paths("a.*.x", tree)))
        self.assertEqual(["a.t1.x"], analysis.expand_paths("a.t1.x", tree))
        self.assertEqual(["arr.0.v", "arr.1.v"], sorted(analysis.expand_paths("arr[].v", tree)))
        self.assertEqual(["early_hold", "early_max"], sorted(analysis.expand_paths("early_*", tree)))
        self.assertEqual([], analysis.expand_paths("a.nope.x", tree))


class LifecycleTests(unittest.TestCase):
    """服务器生命周期：防「开了不关」的判定。喂时间戳即可测，不依赖真实时钟推进。"""

    def test_idle_timeout_trips(self):
        life = editor.Lifecycle(60.0, now=0.0)
        self.assertIsNone(life.should_exit(30.0))
        self.assertEqual("空闲超过 1 分钟", life.should_exit(61.0))

    def test_idle_timeout_can_be_disabled(self):
        life = editor.Lifecycle(0.0, now=0.0)
        self.assertIsNone(life.should_exit(10 ** 6))

    def test_page_close_exits_only_after_grace(self):
        life = editor.Lifecycle(0.0, now=0.0)
        life.ping("a", 1.0)
        life.bye("a", 2.0)
        self.assertIsNone(life.should_exit(2.0 + editor.PAGE_CLOSE_GRACE - 0.1))
        self.assertEqual("页面已关闭", life.should_exit(2.0 + editor.PAGE_CLOSE_GRACE + 0.1))

    def test_second_page_keeps_server_alive(self):
        # 多标签页：关掉一个不该把服务带走
        life = editor.Lifecycle(0.0, now=0.0)
        life.ping("a", 1.0)
        life.ping("b", 1.0)
        life.bye("a", 2.0)
        self.assertIsNone(life.should_exit(100.0))

    def test_dead_page_is_forgotten_by_ttl(self):
        # 页面崩了（收不到 bye）：靠心跳超时把它忘掉，且不因此判成「页面已关闭」而误退
        life = editor.Lifecycle(0.0, now=0.0)
        life.ping("a", 0.0)
        self.assertIsNone(life.should_exit(editor.PAGE_TTL + 1.0))
        self.assertEqual({}, life.pages)
        self.assertIsNone(life.should_exit(editor.PAGE_TTL + 2.0))

    def test_ping_clears_close_flag(self):
        # 刷新页面会先 bye 再立刻 ping：这次 ping 必须把关闭计时清掉
        life = editor.Lifecycle(0.0, now=0.0)
        life.ping("a", 0.0)
        life.bye("a", 1.0)
        life.ping("a", 2.0)
        self.assertIsNone(life.should_exit(3.0))


class StateCacheTests(unittest.TestCase):
    """/api/state 的响应缓存：键说明展开约 580 条，缓存失效写错会让界面显示旧值（静默错误）。"""

    def setUp(self):
        self.dir = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.dir, ignore_errors=True)
        self.path = self.dir / "balance.json"
        shutil.copy(BALANCE, self.path)
        self.editor = editor.Editor(self.path)

    def test_cache_hits_then_invalidates_on_save(self):
        first = self.editor.state_json()
        self.assertIs(first, self.editor.state_json())        # 同一份文件：复用同一份字节
        payload = copy.deepcopy(self.editor.read_balance())
        payload["player"]["max_speed"] = 999
        self.editor.save(payload)
        self.assertIsNot(first, self.editor.state_json())     # 落盘后必须重算
        self.assertIn(b'"max_speed": 999', self.editor.state_json())


class PresetEndpointTests(unittest.TestCase):
    def setUp(self):
        self.dir = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.dir, ignore_errors=True)
        self.path = self.dir / "balance.json"
        shutil.copy(BALANCE, self.path)
        self.editor = editor.Editor(self.path)

    def test_presets_list_shape(self):
        presets = self.editor.presets()
        self.assertTrue(presets, "预设清单为空：balance_presets.json 缺失或格式变了")
        for preset in presets:
            self.assertEqual({"id", "name", "desc", "tags"}, set(preset))

    def test_unknown_preset_is_refused(self):
        code, body = self.editor.preset_plan("no-such-preset", self.editor.read_balance())
        self.assertEqual(404, code)
        self.assertFalse(body["ok"])

    def test_plan_returns_changes_without_skips(self):
        code, body = self.editor.preset_plan("relaxed", self.editor.read_balance())
        self.assertEqual(200, code)
        self.assertTrue(body["changes"])
        self.assertEqual([], body["skipped"])

    def test_plan_rejects_non_object_payload(self):
        code, body = self.editor.preset_plan("relaxed", ["not", "a", "tree"])
        self.assertEqual(400, code)
        self.assertFalse(body["ok"])

    def test_origin_reads_committed_version(self):
        code, body = editor.Editor(BALANCE).origin_balance()
        if code == 404 and "git" in str(body.get("message", "")):
            self.skipTest("本机没有可用的 git")
        self.assertEqual(200, code, body)
        self.assertIn("player", body["balance"])


if __name__ == "__main__":
    unittest.main()
